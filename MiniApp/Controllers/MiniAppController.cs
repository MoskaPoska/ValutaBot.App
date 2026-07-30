using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Web;
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

    public record OhlcCandle(double Open, double High, double Low, double Close, double Volume);

    public static void Start(string[] args, int port = 5000)
    {
        Console.WriteLine("=====================================================");
        Console.WriteLine("[Live Core] TradeBE_bot вЂ” MiniApp Server");
        Console.WriteLine($"[+] Port: {port}");
        Console.WriteLine("=====================================================");

        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddCors(options =>
        {
            options.AddPolicy("AllowMiniApp", p => p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
        });
        builder.Services.AddHostedService<TelegramBotService>();

        // Launch Real-Time WebSocket stream for major CME proxy forex streams (0ms latency)
        string[] topStreamSymbols = { "BTCUSDT", "ETHUSDT", "EURUSDT", "GBPUSDT", "AUDUSDT" };
        BinanceWebSocketStream.StartStream(topStreamSymbols, "1m");

        // Init Telegram notifier from config or env (set in Railway dashboard)
        TelegramNotifier.Init(builder.Configuration["TelegramBotToken"] ?? Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN"));

        // Init LightGBM Python ML microservice URL
        MLPythonService.Init(builder.Configuration["MLService:BaseUrl"] ?? Environment.GetEnvironmentVariable("ML_SERVICE_URL") ?? "http://localhost:8765");

        var app = builder.Build();
        app.UseCors("AllowMiniApp");
        app.UseMiddleware<TokenBucketRateLimiterMiddleware>();
        app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(30) });

        app.MapGet("/", async (HttpContext context) =>
        {
            context.Response.ContentType = "text/html; charset=utf-8";
            
            bool isNgrok = (context.Request.Host.Value ?? "").Contains("ngrok", StringComparison.OrdinalIgnoreCase);
            if (isNgrok &&
                !context.Request.Headers.ContainsKey("ngrok-skip-browser-warning") &&
                !context.Request.Query.ContainsKey("ngrok_passed"))
            {
                string bypassScript = $@"<!DOCTYPE html><html><head><script>
                        var xhr = new XMLHttpRequest();
                        xhr.open('GET', window.location.href, true);
                        xhr.setRequestHeader('ngrok-skip-browser-warning', 'true');
                        xhr.onreadystatechange = function () {{ if (xhr.readyState === 4) {{ var url = new URL(window.location.href); url.searchParams.set('ngrok_passed', '1'); window.location.href = url.toString(); }} }};
                        xhr.send();
                    </script></head><body style='background:#0d0e1e; display:flex; justify-content:center; align-items:center; height:100vh; color:#8a4bfb; font-family:sans-serif;'>Р—Р°РіСЂСѓР·РєР° С‚РµСЂРјРёРЅР°Р»Р°...</body></html>";
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

            if (AuthService.IsRateLimited(context, out string? limitError))
                return Results.Json(new { error = limitError }, statusCode: 429);

            if (string.IsNullOrWhiteSpace(asset) || string.IsNullOrWhiteSpace(timeframe))
                return Results.Json(new { error = "asset and timeframe are required" });

            string cleanAsset = AssetSanitizer.Sanitize(asset);
            string tf = timeframe.ToLower().Trim();
            Console.WriteLine($"[ANALYZE] {cleanAsset} | TF: {timeframe}");

            try
            {
                var taEngine = new TechnicalAnalysisEngine();
                var handler = new ValutaBot.MiniApp.CQRS.Handlers.GetMarketAnalysisQueryHandler(
                    taEngine, MarketDataFetcher.Instance, new WalkForwardValidationEngine(taEngine),
                    new ConfluenceMatrixEngine(MarketDataFetcher.Instance, taEngine), new AdaptiveExpiryEngine());
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
                    message = ex.Message,
                    details = ex.ToString()
                });
            }
        });

        app.MapGet("/api/stats", HandleGetStats);
        app.MapGet("/api/signal-stats", HandleGetSignalStats);

        app.MapGet("/api/fear-greed", async (HttpContext context) =>
        {
            context.Response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
            var (isAuthorized, authError) = await AuthService.IsRequestAuthorized(context);
            if (!isAuthorized)
                return Results.Json(new { error = authError }, statusCode: 401);

            var fng = await GetFearGreedIndex();
            return Results.Json(fng);
        });





        /* в”Ђв”Ђв”Ђ Postback Endpoint в”Ђв”Ђв”Ђ */
        app.MapGet("/api/postback", async (HttpContext context) =>
        {
            var query = context.Request.Query;
            
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

            Console.WriteLine($"[Postback] Received: pocketId={pocketId}, chatId={chatId}, status={status}, deposit={deposit}");

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



    /* в”Ђв”Ђв”Ђ Indicators в”Ђв”Ђв”Ђ */
    /* в”Ђв”Ђв”Ђ Fear & Greed Index в”Ђв”Ђв”Ђ */

    private static readonly HttpClient _fngHttp = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

    private static async Task<object> GetFearGreedIndex()
    {
        const string cacheKey = "fear_greed";
        if (_cache.TryGetValue(cacheKey, out object? cached))
            return cached!;

        try
        {
            var json = await _fngHttp.GetStringAsync("https://api.alternative.me/fng/?limit=1");
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




