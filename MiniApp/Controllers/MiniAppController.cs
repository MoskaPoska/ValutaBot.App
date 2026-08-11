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

    public static System.Net.Http.IHttpClientFactory? HttpFactory { get; set; }

    public static void Start(string[] args, int port = 5000)
    {
        Console.WriteLine("=====================================================");
        Console.WriteLine("[Live Core] TradeBE_bot — MiniApp Server");

        string? envPort = Environment.GetEnvironmentVariable("PORT");
        if (!string.IsNullOrEmpty(envPort) && int.TryParse(envPort, out int parsedPort))
        {
            port = parsedPort;
        }

        Console.WriteLine($"[+] Port: {port}");
        Console.WriteLine("=====================================================");

        var builder = WebApplication.CreateBuilder(args);
        
        var botSettings = builder.Configuration.GetSection("TradingBotSettings").Get<TradingBotSettings>() ?? new TradingBotSettings();
        builder.Services.Configure<TradingBotSettings>(builder.Configuration.GetSection("TradingBotSettings"));

        // Register Engines and Services in DI
        builder.Services.AddSingleton<MarketDataFetcher>();
        builder.Services.AddSingleton<TechnicalAnalysisEngine>();
        builder.Services.AddSingleton<ITechnicalAnalysisEngine>(sp => sp.GetRequiredService<TechnicalAnalysisEngine>());
        builder.Services.AddSingleton<IMathEngine>(sp => sp.GetRequiredService<TechnicalAnalysisEngine>());
        builder.Services.AddSingleton<IMarketAnalyzer>(sp => sp.GetRequiredService<TechnicalAnalysisEngine>());
        builder.Services.AddSingleton<IRiskGatekeeper>(sp => sp.GetRequiredService<TechnicalAnalysisEngine>());
        builder.Services.AddSingleton<IWalkForwardValidationEngine, WalkForwardValidationEngine>();
        builder.Services.AddSingleton<IAutoCalibrationEngine, AutoCalibrationEngine>();
        builder.Services.AddSingleton<IConfluenceMatrixEngine, ConfluenceMatrixEngine>();
        builder.Services.AddSingleton<TradeTimeoutEngine>();
        builder.Services.AddSingleton<ITradeTimeoutEngine>(sp => sp.GetRequiredService<TradeTimeoutEngine>());
        builder.Services.AddSingleton<MonteCarloEngine>();
        builder.Services.AddSingleton<IMonteCarloEngine>(sp => sp.GetRequiredService<MonteCarloEngine>());
        
        // Register CQRS Handlers
        builder.Services.AddTransient<ValutaBot.MiniApp.CQRS.Handlers.GetMarketAnalysisQueryHandler>();
        builder.Services.AddTransient<ValutaBot.MiniApp.Features.MarketAnalysis.IMarketAnalysisOrchestrator, ValutaBot.MiniApp.Features.MarketAnalysis.MarketAnalysisOrchestrator>();

        builder.Services.AddCors(options =>
        {
            options.AddPolicy("AllowMiniApp", p => p
                .SetIsOriginAllowed(origin => 
                {
                    if (string.IsNullOrEmpty(origin)) return false;
                    var host = new Uri(origin).Host.ToLowerInvariant();
                    return host == "web.telegram.org" || 
                           host.EndsWith("ngrok-free.dev") || 
                           host.EndsWith("ngrok.io") ||
                           host.EndsWith("railway.app") ||
                           host == "localhost" || 
                           host == "127.0.0.1";
                })
                .WithMethods("GET", "POST", "OPTIONS")
                .WithHeaders("X-Telegram-Init-Data", "Content-Type", "Accept"));
        });
        builder.Services.AddHostedService<TelegramBotService>();
        builder.Services.AddHttpClient("Binance").AddStandardResilienceHandler(options =>
        {
            options.Retry.MaxRetryAttempts = botSettings.MaxHttpRetries;
            options.Retry.Delay = TimeSpan.FromMilliseconds(botSettings.HttpRetryDelayMs);
            options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(3);
            options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(5);
        });
        builder.Services.AddHttpClient("TwelveData").AddStandardResilienceHandler(options =>
        {
            options.Retry.MaxRetryAttempts = botSettings.MaxHttpRetries;
            options.Retry.Delay = TimeSpan.FromMilliseconds(botSettings.HttpRetryDelayMs);
            options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(3);
            options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(5);
        });
        builder.Services.AddHttpClient("FNG").AddStandardResilienceHandler();
        builder.Services.AddHttpClient("MLPythonService").AddStandardResilienceHandler(options =>
        {
            options.Retry.MaxRetryAttempts = 1;
            options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(botSettings.FastFailTimeoutSeconds);
            options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(botSettings.FastFailTimeoutSeconds);
        });
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
                await context.HttpContext.Response.WriteAsync("{\"error\":\"РЎР»РёС€РєРѕРј РјРЅРѕРіРѕ Р·Р°РїСЂРѕСЃРѕРІ. РџРѕРґРѕР¶РґРёС‚Рµ РЅРµСЃРєРѕР»СЊРєРѕ СЃРµРєСѓРЅРґ.\"}");
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

        bool isWeekend = DateTime.UtcNow.DayOfWeek == DayOfWeek.Saturday || DateTime.UtcNow.DayOfWeek == DayOfWeek.Sunday;
        if (isWeekend)
        {
            // Launch Real-Time WebSocket stream for major CME proxy forex streams (0ms latency)
            string[] topStreamSymbols = { "EURUSDT", "GBPUSDT", "AUDUSDT" };
            BinanceWebSocketStream.StartStream(topStreamSymbols, "1m");
        }

        // Init Telegram notifier from config or env (set in Railway dashboard)
        TelegramNotifier.Init(builder.Configuration["TelegramBotToken"] ?? Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN"));

        // Init LightGBM Python ML microservice URL
        MLPythonService.Init(builder.Configuration["MLService:BaseUrl"] ?? Environment.GetEnvironmentVariable("ML_SERVICE_URL") ?? "http://localhost:8765");

        builder.Environment.WebRootPath = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "MiniApp", "wwwroot");
        var app = builder.Build();
        
        TradeOutcomeTracker.CalibrationEngine = app.Services.GetRequiredService<IAutoCalibrationEngine>();
        TradeOutcomeTracker.WfEngine = app.Services.GetRequiredService<IWalkForwardValidationEngine>();

        HttpFactory = app.Services.GetRequiredService<System.Net.Http.IHttpClientFactory>();
        app.UseStaticFiles();
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
                    </script></head><body style='background:#0d0e1e; display:flex; justify-content:center; align-items:center; height:100vh; color:#8a4bfb; font-family:sans-serif;'>Р—Р°РіСЂСѓР·РєР° С‚РµСЂРјРёРЅР°Р»Р°...</body></html>";
                await context.Response.WriteAsync(bypassScript);
                return;
            }
            await context.Response.SendFileAsync(System.IO.Path.Combine(app.Environment.WebRootPath, "index.html"));
        });



        app.MapGet("/api/analyze", async Task<IResult> (HttpContext context, string? asset, string? timeframe) =>
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
                // C1 FIX: Reuse shared singleton engines from DI
                var handler = context.RequestServices.GetRequiredService<ValutaBot.MiniApp.CQRS.Handlers.GetMarketAnalysisQueryHandler>();
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
                    error = ex.Message
                });
            }
        }).RequireRateLimiting("Global");

        app.MapGet("/api/stats", (Delegate)HandleGetStats).RequireRateLimiting("Global");
        app.MapGet("/api/signal-stats", (Delegate)HandleGetSignalStats).RequireRateLimiting("Global");

        app.MapGet("/api/fear-greed", async Task<IResult> (HttpContext context) =>
        {
            context.Response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
            var (isAuthorized, authError) = await AuthService.IsRequestAuthorized(context);
            if (!isAuthorized)
                return Results.Json(new { error = authError }, statusCode: 401);

            var fng = await GetFearGreedIndex();
            return Results.Json(fng);
        });

        /* в”Ђв”Ђв”Ђ Postback Endpoint в”Ђв”Ђв”Ђ */
        app.MapGet("/api/postback", async Task<IResult> (HttpContext context) =>
        {
            var query = context.Request.Query;
            
            // SECURITY: Verify Postback Secret
            string expectedSecret = Environment.GetEnvironmentVariable("POSTBACK_SECRET") ?? "";
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

            BotLogger.Info($"[Postback рџ”’] Verified Postback: pocketId={pocketId}, chatId={chatId}, status={status}, deposit={deposit}");

            await TelegramBotService.ProcessPostback(chatId, pocketId, status, deposit);

            return Results.Ok(new { success = true, message = "Postback processed successfully" });
        });




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



    /* в”Ђв”Ђв”Ђ Indicators в”Ђв”Ђв”Ђ */
    /* в”Ђв”Ђв”Ђ Fear & Greed Index в”Ђв”Ђв”Ђ */

    

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



