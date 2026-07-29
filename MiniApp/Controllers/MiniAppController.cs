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

    // OHLC candle cache for Claude pattern analysis (filled during data fetch, read by ClaudeSignalService)
    public record OhlcCandle(double Open, double High, double Low, double Close, double Volume);
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, OhlcCandle[]> _ohlcCache = new();
    public static OhlcCandle[]? GetOhlcCandles(string key) => _ohlcCache.TryGetValue(key, out var v) ? v : null;
    public static void SetOhlcCandles(string key, OhlcCandle[] candles) => _ohlcCache[key] = candles;

    public static void Start(string[] args, int port = 5000)
    {
        Console.WriteLine("=====================================================");
        Console.WriteLine("[Live Core] TradeBE_bot — MiniApp Server");
        Console.WriteLine($"[+] Port: {port}");
        Console.WriteLine("=====================================================");

        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddCors(options =>
        {
            options.AddPolicy("AllowMiniApp", p => p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
        });
        builder.Services.AddHostedService<MarketDataService>();
        builder.Services.AddHostedService<LiquidationHeatmapService>();
        builder.Services.AddHostedService<TwelveDataWebSocketStream>();
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
                    </script></head><body style='background:#0d0e1e; display:flex; justify-content:center; align-items:center; height:100vh; color:#8a4bfb; font-family:sans-serif;'>Загрузка терминала...</body></html>";
                await context.Response.WriteAsync(bypassScript);
                return;
            }
            await context.Response.WriteAsync(MiniAppUI.GetHtml());
        });



        app.MapGet("/api/analyze", async (HttpContext context, string? asset, string? timeframe) =>
        {
            context.Response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
            if (!context.RequestServices.GetRequiredService<IAuthService>().IsRequestAuthorized(context, out string? authError))
                return Results.Json(new { error = authError }, statusCode: 401);

            if (context.RequestServices.GetRequiredService<IAuthService>().IsRateLimited(context, out string? limitError))
                return Results.Json(new { error = limitError }, statusCode: 429);

            if (string.IsNullOrWhiteSpace(asset) || string.IsNullOrWhiteSpace(timeframe))
                return Results.Json(new { error = "asset and timeframe are required" });

            string cleanAsset = AssetSanitizer.Sanitize(asset);
            string tf = timeframe.ToLower().Trim();
            Console.WriteLine($"[ANALYZE] {cleanAsset} | TF: {timeframe}");

            try
            {
                var result = await context.RequestServices.GetRequiredService<IAnalysisOrchestrator>().ExecuteBinanceAnalysis(cleanAsset, tf);
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
            if (!context.RequestServices.GetRequiredService<IAuthService>().IsRequestAuthorized(context, out string? authError))
                return Results.Json(new { error = authError }, statusCode: 401);

            var fng = await GetFearGreedIndex();
            return Results.Json(fng);
        });

        app.MapGet("/api/market-status", (HttpContext context) =>
        {
            context.Response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
            if (!context.RequestServices.GetRequiredService<IAuthService>().IsRequestAuthorized(context, out string? authError))
                return Results.Json(new { error = authError }, statusCode: 401);

            var latest = MarketDataService.GetLatestPrices();
            var alerts = MarketDataService.GetRecentAlerts();
            return Results.Json(new { prices = latest, alerts });
        });

        app.MapGet("/api/liquidations", (HttpContext context) =>
        {
            context.Response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
            if (!context.RequestServices.GetRequiredService<IAuthService>().IsRequestAuthorized(context, out string? authError))
                return Results.Json(new { error = authError }, statusCode: 401);

            return Results.Json(LiquidationHeatmapService.GetHeatmapData());
        });

        app.MapGet("/api/time", (HttpContext context) =>
        {
            context.Response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
            return Results.Json(new { t = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() });
        });

        app.Map("/ws/prices", async (HttpContext context, string? asset) =>
        {
            if (string.IsNullOrEmpty(asset))
            {
                context.Response.StatusCode = 400;
                await context.Response.WriteAsync("asset parameter is required");
                return;
            }

            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = 400;
                return;
            }

            using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
            string clientId = Guid.NewGuid().ToString();

            try
            {
                await TwelveDataWebSocketStream.RegisterClientAsync(asset, clientId, webSocket);

                var buffer = new byte[1024 * 4];
                while (webSocket.State == System.Net.WebSockets.WebSocketState.Open)
                {
                    var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                    if (result.MessageType == System.Net.WebSockets.WebSocketMessageType.Close)
                    {
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WS Route] Connection error for {clientId}: {ex.Message}");
            }
            finally
            {
                TwelveDataWebSocketStream.UnregisterClient(asset, clientId);
                if (webSocket.State != System.Net.WebSockets.WebSocketState.Aborted && webSocket.State != System.Net.WebSockets.WebSocketState.Closed)
                {
                    try
                    {
                        await webSocket.CloseAsync(System.Net.WebSockets.WebSocketCloseStatus.NormalClosure, "Closed by server", CancellationToken.None);
                    }
                    catch { }
                }
            }
        });





        /* ─── Postback Endpoint ─── */
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
        _ = TwelveDataWebSocketStream.StartBackgroundStreamingAsync();

        app.Run($"http://0.0.0.0:{port}");
    }

    public static async Task<(double[] prices, double[] volumes)> GetSubMinuteCandles(string? symbol, string asset, string timeframe, int limit)
    {
        string tdSymbol = TwelveDataService.ConvertToTwelveSymbol(asset) ?? asset;
        var ticks = TwelveDataWebSocketStream.GetTicks(tdSymbol);
        int tfSec = TimeframeSeconds(timeframe);

        List<OhlcCandle> aggregatedCandles = new();

        if (ticks.Length > 0)
        {
            // Sort ticks chronologically
            var sortedTicks = ticks.OrderBy(t => t.timestamp).ToList();
            
            long firstBucket = ((DateTimeOffset)sortedTicks[0].timestamp).ToUnixTimeSeconds() / tfSec;
            long lastBucket = ((DateTimeOffset)DateTime.UtcNow).ToUnixTimeSeconds() / tfSec;

            // Group ticks by bucket
            var tickBuckets = sortedTicks
                .GroupBy(t => ((DateTimeOffset)t.timestamp).ToUnixTimeSeconds() / tfSec)
                .ToDictionary(g => g.Key, g => g.ToList());

            double lastClose = sortedTicks[0].price;

            for (long b = firstBucket; b <= lastBucket; b++)
            {
                if (tickBuckets.TryGetValue(b, out var bTicks))
                {
                    double open = bTicks[0].price;
                    double close = bTicks[^1].price;
                    double high = bTicks.Max(t => t.price);
                    double low = bTicks.Min(t => t.price);
                    double vol = bTicks.Count;

                    aggregatedCandles.Add(new OhlcCandle(open, high, low, close, vol));
                    lastClose = close;
                }
                else
                {
                    // Forward fill if bucket is empty
                    aggregatedCandles.Add(new OhlcCandle(lastClose, lastClose, lastClose, lastClose, 0));
                }
            }
        }

        // If we don't have enough candles to satisfy the limit, fetch 1m candles and interpolate them
        if (aggregatedCandles.Count < limit)
        {
            int needed = limit - aggregatedCandles.Count;
            int subCandlesPerMinute = 60 / tfSec;
            int fetchLimit = Math.Max(needed / subCandlesPerMinute + 10, 50);

            try
            {
                var m1Result = await MarketDataFetcher.Instance.FetchBinanceWithFallback(symbol, "1m", asset, fetchLimit, 10);
                string ohlcKey = symbol != null ? $"{symbol}_1m" : $"{asset}_1m";
                var m1Ohlc = GetOhlcCandles(ohlcKey);

                if (m1Ohlc != null && m1Ohlc.Length > 0)
                {
                    List<OhlcCandle> interpolated = new();

                    foreach (var mCandle in m1Ohlc)
                    {
                        double startPrice = mCandle.Open;
                        double endPrice = mCandle.Close;
                        double range = endPrice - startPrice;

                        for (int i = 0; i < subCandlesPerMinute; i++)
                        {
                            double fractionStart = (double)i / subCandlesPerMinute;
                            double fractionEnd = (double)(i + 1) / subCandlesPerMinute;

                            double open = startPrice + range * fractionStart;
                            double close = startPrice + range * fractionEnd;
                            
                            double high = Math.Max(open, close);
                            double low = Math.Min(open, close);

                            high = Math.Min(high, mCandle.High);
                            low = Math.Max(low, mCandle.Low);

                            interpolated.Add(new OhlcCandle(open, high, low, close, mCandle.Volume / subCandlesPerMinute));
                        }
                    }

                    // Prepend interpolated candles
                    interpolated.AddRange(aggregatedCandles);
                    aggregatedCandles = interpolated;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Aggregator Warning] Interpolation base fetch failed: {ex.Message}");
            }
        }

        // Slice to the requested limit
        if (aggregatedCandles.Count > limit)
        {
            aggregatedCandles = aggregatedCandles.Skip(aggregatedCandles.Count - limit).ToList();
        }

        // Cache the custom sub-minute OHLC candles for indicator/pattern analysis
        string cacheKey = symbol != null ? $"{symbol}_{timeframe.ToLower()}" : $"{asset}_{timeframe.ToLower()}";
        SetOhlcCandles(cacheKey, aggregatedCandles.ToArray());

        var prices = aggregatedCandles.Select(c => c.Close).ToArray();
        var volumes = aggregatedCandles.Select(c => c.Volume).ToArray();
        return (prices, volumes);
    }

    private static string IntervalMap(string tf) => tf.ToLower() switch
    {
        "s3" or "s5" or "s10" or "s15" or "s30" => "1m",
        "m1" => "1m", "m2" => "1m", "m3" => "3m",
        "m5" => "5m", "m15" => "15m", "m30" => "30m",
        "h1" => "1h", "h4" => "4h",
        "d1" => "1d", _ => "1m"
    };

    private static int GetExpiryCandles(string tf) => tf.ToLower() switch
    {
        "s3" or "s5" or "s10" or "s15" or "s30" => 3, // Micro-scalp 3-bar expiry
        "m1" => 3,   // 3 minutes (highly stable for M1 charts)
        "m2" => 2,   // 4 minutes
        "m3" => 2,   // 6 minutes
        "m5" => 3,   // 15 minutes (standard binary options target)
        "m15" => 2,  // 30 minutes
        "m30" => 2,  // 60 minutes
        "h1" => 2,   // 2 hours
        "h4" => 1,
        "d1" => 1,
        _ => 3
    };

    private static int BinanceIntervalToSeconds(string binanceInterval) => binanceInterval.ToLower() switch
    {
        "1m" => 60,
        "3m" => 180,
        "5m" => 300,
        "15m" => 900,
        "30m" => 1800,
        "1h" => 3600,
        "4h" => 14400,
        "1d" => 86400,
        _ => 60
    };

    private static int TimeframeSeconds(string tf) => tf.ToLower() switch
    {
        "s3" => 3, "s5" => 5, "s10" => 10, "s15" => 15, "s30" => 30,
        "m1" => 60, "m2" => 120, "m3" => 180, "m5" => 300,
        "m15" => 900, "m30" => 1800,
        "h1" => 3600, "h4" => 14400,
        "d1" => 86400, _ => 60
    };

    private static string? HigherTf(string tf) => tf.ToLower() switch
    {
        "s3" or "s5" or "s10" or "s15" or "s30" => "m5", // Verify micro-momentum trends against the 5-minute chart
        "m1" => "m5", "m2" => "m5", "m3" => "m5",
        "m5" => "m15", "m15" => "h1", "m30" => "h1",
        "h1" => "h4", "h4" => "d1", _ => null
    };

    private static string? LowerTf(string tf) => tf.ToLower() switch
    {
        "m1" => null, // Prevents duplicate fetching of 1m candles for lower TF
        "m2" => "m1", "m3" => "m1",
        "m5" => "m1", "m15" => "m5", "m30" => "m15",
        "h1" => "m30", "h4" => "h1",
        "d1" => "h4", _ => null
    };



    /* ─── Indicators ─── */


    public static object GetMomentumPrediction(string asset, string tf)
    {
        int expiryCandles = GetExpiryCandles(tf);

        return new
        {
            direction = "NEUTRAL",
            probability = 50,
            duration = $"{tf.ToUpper()} ({expiryCandles} свечи)",
            expiryCandles,
            chartData = Array.Empty<double>(),
            rsi = 50.0,
            ema = 0.0,
            volumeStrength = 0.0,
            tfConflict = false,
            mlDirection = "NEUTRAL",
            mlConfidence = 0,
            newsSentiment = "Нейтрально",
            newsScore = 0,
            newsSummary = "Данные недоступны",
            newsHeadlines = Array.Empty<string>(),
            claudeDirection = "NEUTRAL",
            claudeProbability = 0,
            claudeReasoning = "Недостаточно рыночных данных для вычисления сигнала.",
            aiModel = "Нейтральный режим"
        };
    }

    /* ─── Fear & Greed Index ─── */

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
