using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Polly;
using Polly.Retry;

namespace ValutaBot.MiniApp;

public static partial class MiniAppController
{
    private static readonly IMemoryCache _cache = new MemoryCache(new MemoryCacheOptions());

    public static string? LastExceptionMessage { get; set; }

    public record OhlcCandle(double Open, double High, double Low, double Close, double Volume, DateTime Timestamp = default);

    public static System.Net.Http.IHttpClientFactory? HttpFactory { get; private set; }

    public static void Start(string[] args, int port = 5000)
    {
        Console.WriteLine("=====================================================");
        Console.WriteLine("[Live Core] TradeBE_bot — MiniApp Server");
        Console.WriteLine($"[+] Port: {port}");
        Console.WriteLine("=====================================================");

        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddCors(options =>
        {
            options.AddPolicy("AllowMiniApp", p => p
                .AllowAnyOrigin()
                .WithMethods("GET", "POST", "OPTIONS")
                .WithHeaders("X-Telegram-Init-Data", "Content-Type", "Accept"));
        });
        builder.Services.AddHostedService<TelegramBotService>();
        builder.Services.AddHttpClient("Binance").AddStandardResilienceHandler();
        builder.Services.AddHttpClient("TwelveData").AddStandardResilienceHandler();
        builder.Services.AddHttpClient("FNG").AddStandardResilienceHandler();
        builder.Services.AddHttpClient("MLPythonService").AddStandardResilienceHandler();
        builder.Services.AddHttpClient("Telegram", client => 
        {
            client.Timeout = TimeSpan.FromSeconds(60); // Must be longer than getUpdates timeout=30
        });

        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, token) =>
            {
                context.HttpContext.Response.ContentType = "application/json; charset=utf-8";
                await context.HttpContext.Response.WriteAsync("{\"error\":\"Слишком много запросов. Подождите несколько секунд.\"}");
            };

            options.AddPolicy("Global", context =>
            {
                string ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown_ip";
                string initData = context.Request.Headers["X-Telegram-Init-Data"].ToString();
                string fingerprint = $"{ip}|{initData}";
                
                return RateLimitPartition.GetTokenBucketLimiter(fingerprint, _ =>
                    new TokenBucketRateLimiterOptions
                    {
                        TokenLimit = 10,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0,
                        ReplenishmentPeriod = TimeSpan.FromSeconds(2),
                        TokensPerPeriod = 1,
                        AutoReplenishment = true
                    });
            });
        });

        // Launch Real-Time WebSocket stream for major CME proxy forex streams (0ms latency)
        string[] topStreamSymbols = { "EURUSDT", "GBPUSDT", "AUDUSDT", "USDJPY" };
        BinanceWebSocketStream.StartStream(topStreamSymbols, "1m");

        // Init Telegram notifier from config or env (set in Railway dashboard)
        TelegramNotifier.Init(builder.Configuration["TelegramBotToken"] ?? Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN"));

        // Init LightGBM Python ML microservice URL
        MLPythonService.Init(builder.Configuration["MLService:BaseUrl"] ?? Environment.GetEnvironmentVariable("ML_SERVICE_URL") ?? "http://localhost:8765");

        var app = builder.Build();
        HttpFactory = app.Services.GetRequiredService<System.Net.Http.IHttpClientFactory>();
        app.UseCors("AllowMiniApp");
        
        // SECURITY: Global HTTP Security Headers (prevent MIME-sniffing, XSS, etc.)
        app.Use(async (context, next) =>
        {
            context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
            context.Response.Headers.Append("X-XSS-Protection", "1; mode=block");
            context.Response.Headers.Append("Strict-Transport-Security", "max-age=31536000; includeSubDomains");
            // Allow framing only from Telegram (to allow WebApp to work inside Telegram UI)
            context.Response.Headers.Append("Content-Security-Policy", "frame-ancestors 'self' https://web.telegram.org tg://*");
            await next();
        });

        app.UseRateLimiter();
        app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(30) });

        app.MapGet("/", async (HttpContext context) =>
        {
            context.Response.ContentType = "text/html; charset=utf-8";
            
            bool isNgrok = (context.Request.Host.Value ?? "").Contains("ngrok", StringComparison.OrdinalIgnoreCase);
            if (isNgrok &&
                !context.Request.Headers.ContainsKey("ngrok-skip-browser-warning") &&
                !context.Request.Query.ContainsKey("ngrok_passed"))
            {
                string bypassScript = @"<!DOCTYPE html><html><head><script>
                        var xhr = new XMLHttpRequest();
                        xhr.open('GET', window.location.href, true);
                        xhr.setRequestHeader('ngrok-skip-browser-warning', 'true');
                        xhr.onreadystatechange = function () { if (xhr.readyState === 4) { var url = new URL(window.location.href); url.searchParams.set('ngrok_passed', '1'); window.location.href = url.toString(); } };
                        xhr.send();
                    </script></head><body style='background:#0d0e1e; display:flex; justify-content:center; align-items:center; height:100vh; color:#8a4bfb; font-family:sans-serif;'>Загрузка терминала...</body></html>";
                await context.Response.WriteAsync(bypassScript);
                return;
            }
            await context.Response.WriteAsync(MiniAppUI.GetHtml());
        });



        app.MapGet("/api/analyze", async (HttpContext context, string? asset, string? timeframe) =>
        {
            context.Response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
            var (isAuthorized, authError) = await AuthService.IsRequestAuthorized(context);
            if (!isAuthorized)
                return Results.Json(new { error = authError }, statusCode: 401);

            if (string.IsNullOrWhiteSpace(asset) || string.IsNullOrWhiteSpace(timeframe))
                return Results.Json(new { error = "asset and timeframe are required" });

            string cleanAsset = AssetSanitizer.Sanitize(asset);
            string tf = timeframe.ToLower().Trim();
            Console.WriteLine($"[ANALYZE] {cleanAsset} | TF: {timeframe}");

            try
            {
                var taEngine = new TechnicalAnalysisEngine();
                var handler = new ValutaBot.MiniApp.CQRS.Handlers.GetMarketAnalysisQueryHandler(new TechnicalAnalysisEngine(), new TechnicalAnalysisEngine(), new TechnicalAnalysisEngine(), new MarketDataFetcher(), new WalkForwardValidationEngine(), new ConfluenceMatrixEngine(new MarketDataFetcher(), new TechnicalAnalysisEngine()), new TradeTimeoutEngine());
                var result = await handler.Handle(new ValutaBot.MiniApp.CQRS.Queries.GetMarketAnalysisQuery(cleanAsset, tf), context.RequestAborted);
                // Serialize manually to catch float.NaN or reference errors during serialization
                var options = new JsonSerializerOptions
                {
                    NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals
                };
                var json = JsonSerializer.Serialize(result, options);
                return Results.Content(json, "application/json", Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[API ERR] /api/analyze failed: {ex}");
                return Results.Json(new
                {
                    error = ex.Message,
                    message = ex.ToString()
                });
            }
        }).RequireRateLimiting("Global");

        app.MapGet("/api/stats", HandleGetStats).RequireRateLimiting("Global");
        app.MapGet("/api/signal-stats", HandleGetSignalStats).RequireRateLimiting("Global");

        app.MapGet("/api/fear-greed", async (HttpContext context) =>
        {
            context.Response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
            var (isAuthorized, authError) = await AuthService.IsRequestAuthorized(context);
            if (!isAuthorized)
                return Results.Json(new { error = authError }, statusCode: 401);

            var fng = await GetFearGreedIndex();
            return Results.Json(fng);
        });

        /* ─── Postback Endpoint ─── */
        app.MapGet("/api/postback", async (HttpContext context) =>
        {
            var query = context.Request.Query;
            
            // SECURITY: Verify Postback Secret
            string expectedSecret = Environment.GetEnvironmentVariable("POSTBACK_SECRET") ?? "test_secret_123";
            string providedSecret = query.TryGetValue("secret", out var secVal) ? secVal.ToString().Trim() : "";
            
            if (string.IsNullOrEmpty(providedSecret) || providedSecret != expectedSecret)
            {
                BotLogger.Warn($"[Security] Unauthorized postback attempt blocked (Invalid Secret). IP: {context.Connection.RemoteIpAddress}");
                return Results.Unauthorized();
            }

            string pocketId = query.TryGetValue("pocketId", out var pVal) ? pVal.ToString().Trim() : "";
            string status = query.TryGetValue("status", out var sVal) ? sVal.ToString().Trim().ToLower() : "";
            
            double deposit = 0;
            if (query.TryGetValue("deposit", out var dVal))
            {
                double.TryParse(dVal.ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out deposit);
            }

            long chatId = 0;
            if (query.TryGetValue("chatId", out var cVal))
            {
                long.TryParse(cVal.ToString(), out chatId);
            }

            if (string.IsNullOrEmpty(pocketId))
            {
                return Results.BadRequest(new { success = false, error = "pocketId is required" });
            }

            BotLogger.Info($"[Postback 🔒] Verified Postback: pocketId={pocketId}, chatId={chatId}, status={status}, deposit={deposit}");

            await TelegramBotService.ProcessPostback(chatId, pocketId, status, deposit);

            return Results.Ok(new { success = true, message = "Postback processed successfully" });
        });

        string? mlServiceUrl = builder.Configuration["MLService:BaseUrl"];
        if (string.IsNullOrWhiteSpace(mlServiceUrl))
            mlServiceUrl = Environment.GetEnvironmentVariable("ML_SERVICE_URL");
        if (string.IsNullOrWhiteSpace(mlServiceUrl))
            mlServiceUrl = string.Empty;
        
        MLPythonService.Init(mlServiceUrl);


        // Start background TwelveData WebSocket connection immediately to start accumulating ticks

        app.Run($"http://0.0.0.0:{port}");
    }
    private static string? LowerTf(string tf) => tf.ToLower() switch
    {
        "m1" => null, // Prevents duplicate fetching of 1m candles for lower TF
        "m2" => "m1", "m3" => "m1",
        "m5" => "m1", "m15" => "m5", "m30" => "m15",
        "h1" => "m30", "h4" => "h1",
        "d1" => "h4", _ => null
    };



    /* ─── Indicators ─── */
    /* ─── Fear & Greed Index ─── */

    

    private static async Task<object> GetFearGreedIndex()
    {
        const string cacheKey = "fear_greed";
        if (_cache.TryGetValue(cacheKey, out object? cached))
            return cached!;

        try
        {
            var json = await HttpFactory!.CreateClient("FNG").GetStringAsync("https://api.alternative.me/fng/?limit=1");
            using var doc = JsonDocument.Parse(json);
            var data = doc.RootElement.GetProperty("data")[0];
            var result = new
            {
                value = int.TryParse(data.GetProperty("value").GetString(), out var v) ? v : 50,
                classification = data.GetProperty("value_classification").GetString() ?? "Neutral"
            };
            _cache.Set(cacheKey, (object)result, TimeSpan.FromHours(1));
            return result;
        }
        catch
        {
            return new { value = 50, classification = "Neutral" };
        }
    }
}
