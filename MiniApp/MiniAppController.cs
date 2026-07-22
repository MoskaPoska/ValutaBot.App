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
    private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
    private static readonly IMemoryCache _cache = new MemoryCache(new MemoryCacheOptions());
    // Random.Shared is used directly for thread-safety.
    private static readonly AsyncRetryPolicy _retryPolicy = Policy
        .Handle<HttpRequestException>()
        .Or<TaskCanceledException>()
        .WaitAndRetryAsync(3, attempt => TimeSpan.FromMilliseconds(500 * attempt));

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
        builder.Services.AddHostedService<TelegramBotService>();

        // Init Telegram notifier from config or env (set in Railway dashboard)
        TelegramNotifier.Init(builder.Configuration["TelegramBotToken"] ?? Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN"));

        // Init LightGBM Python ML microservice URL
        MLPythonService.Init(builder.Configuration["MLService:BaseUrl"] ?? Environment.GetEnvironmentVariable("ML_SERVICE_URL") ?? "http://localhost:8765");

        var app = builder.Build();
        app.UseCors("AllowMiniApp");
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
            if (!IsRequestAuthorized(context, out string? authError))
                return Results.Json(new { error = authError }, statusCode: 401);

            if (IsRateLimited(context, out string? limitError))
                return Results.Json(new { error = limitError }, statusCode: 429);

            if (string.IsNullOrWhiteSpace(asset) || string.IsNullOrWhiteSpace(timeframe))
                return Results.Json(new { error = "asset and timeframe are required" });

            string cleanAsset = SanitizeAsset(asset);
            string tf = timeframe.ToLower().Trim();
            Console.WriteLine($"[ANALYZE] {cleanAsset} | TF: {timeframe}");

            try
            {
                var result = await ExecuteBinanceAnalysis(cleanAsset, tf);
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

        app.MapGet("/api/stats", (HttpContext context) =>
        {
            context.Response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
            var overall = SignalTracker.GetOverallStats();
            var allStats = SignalTracker.GetAllStats()
                .Where(s => s.Key != "ALL" && s.Verified > 0)
                .OrderByDescending(s => s.Verified)
                .Select(s => new
                {
                    key       = s.Key,
                    verified  = s.Verified,
                    correct   = s.Correct,
                    incorrect = s.Incorrect,
                    winRate   = s.WinRate,
                    pending   = s.Pending
                });

            var signalSources = SignalTracker.GetSignalStats()
                .Select(s => new
                {
                    name      = s.name,
                    agreeRate = s.agreeRatePct,
                    weight    = s.weight,
                    count     = s.count
                });

            var recent = SignalTracker.GetRecentArchive(20)
                .Select(r => new
                {
                    asset     = r.Asset,
                    tf        = r.Timeframe,
                    direction = r.Direction,
                    entry     = Math.Round(r.EntryPrice, 5),
                    exit      = r.ExitPrice.HasValue ? Math.Round(r.ExitPrice.Value, 5) : (double?)null,
                    pnlBps    = r.PnlBps,
                    correct   = r.WasCorrect,
                    at        = r.CreatedAt.ToString("HH:mm:ss")
                });

            return Results.Json(new
            {
                overall = new
                {
                    winRate   = overall.HasData ? overall.WinRate : (double?)null,
                    verified  = overall.Verified,
                    correct   = overall.Correct,
                    incorrect = overall.Incorrect,
                    pending   = SignalTracker.GetPendingCount(),
                    hasData   = overall.HasData
                },
                byAsset       = allStats,
                signalSources,
                recentSignals = recent
            });
        });

        app.MapGet("/api/fear-greed", async (HttpContext context) =>
        {
            context.Response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
            if (!IsRequestAuthorized(context, out string? authError))
                return Results.Json(new { error = authError }, statusCode: 401);

            var fng = await GetFearGreedIndex();
            return Results.Json(fng);
        });

        app.MapGet("/api/market-status", (HttpContext context) =>
        {
            context.Response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
            if (!IsRequestAuthorized(context, out string? authError))
                return Results.Json(new { error = authError }, statusCode: 401);

            var latest = MarketDataService.GetLatestPrices();
            var alerts = MarketDataService.GetRecentAlerts();
            return Results.Json(new { prices = latest, alerts });
        });

        app.MapGet("/api/liquidations", (HttpContext context) =>
        {
            context.Response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
            if (!IsRequestAuthorized(context, out string? authError))
                return Results.Json(new { error = authError }, statusCode: 401);

            return Results.Json(LiquidationHeatmapService.GetHeatmapData());
        });

        app.MapGet("/api/signal-stats", (HttpContext context) =>
        {
            context.Response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
            if (!IsRequestAuthorized(context, out string? authError))
                return Results.Json(new { error = authError }, statusCode: 401);

            return Results.Json(new
            {
                accuracy = SignalTracker.GetOverallStats().WinRate,
                signals = SignalTracker.GetSignalStats()
            });
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
                await TwelveDataWebSocketManager.RegisterClientAsync(asset, clientId, webSocket);

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
                TwelveDataWebSocketManager.UnregisterClient(asset, clientId);
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

        /* ─── Alerts ─── */
        app.MapGet("/api/alerts", (HttpContext context) => 
        {
            context.Response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
            if (!IsRequestAuthorized(context, out string? authError))
                return Results.Json(new { error = authError }, statusCode: 401);

            return Results.Json(AlertService.GetAll());
        });

        app.MapPost("/api/alerts", async (HttpContext context) =>
        {
            if (!IsRequestAuthorized(context, out string? authError))
                return Results.Json(new { error = authError }, statusCode: 401);

            var rule = await context.Request.ReadFromJsonAsync<AlertRule>();
            if (rule == null) return Results.BadRequest();
            var created = AlertService.Add(rule);
            return Results.Json(created);
        });

        app.MapDelete("/api/alerts/{id}", (HttpContext context, string id) =>
        {
            if (!IsRequestAuthorized(context, out string? authError))
                return Results.Json(new { error = authError }, statusCode: 401);

            bool ok = AlertService.Remove(id);
            return ok ? Results.Ok() : Results.NotFound();
        });

        app.MapPost("/api/alerts/chatid", async (HttpContext context) =>
        {
            if (!IsRequestAuthorized(context, out string? authError))
                return Results.Json(new { error = authError }, statusCode: 401);

            var body = await context.Request.ReadFromJsonAsync<Dictionary<string, long>>();
            if (body != null && body.TryGetValue("chatId", out var chatId))
                AlertService.SetDefaultChatId(chatId);
            return Results.Ok();
        });

        /* ─── Test Claude Diagnostics ─── */
        app.MapGet("/api/test-claude", async (HttpContext context) =>
        {
            try
            {
                string apiKey = ClaudeSignalService.GetOpenRouterApiKey();
                if (string.IsNullOrEmpty(apiKey))
                {
                    return Results.Json(new { error = "No API key configured" });
                }

                var body = new
                {
                    model = "anthropic/claude-sonnet-5",
                    messages = new[]
                    {
                        new { role = "user", content = "Hello, respond with 1 word." }
                    },
                    temperature = 0.2,
                    max_tokens = 10
                };

                var json = JsonSerializer.Serialize(body);
                using var request = new HttpRequestMessage(HttpMethod.Post, "https://openrouter.ai/api/v1/chat/completions")
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
                request.Headers.Add("HTTP-Referer", "https://valutabotapp-production.up.railway.app");
                request.Headers.Add("X-Title", "ValutaBot");

                using var response = await _httpClient.SendAsync(request);
                string responseBody = await response.Content.ReadAsStringAsync();
                
                ClaudeSignalService.ResetCircuitBreaker();
                string claudeTestResult = "";
                try
                {
                    var testRes = await ClaudeSignalService.AnalyzeSignal(
                        "EUR/USD OTC", new double[50], new double[50],
                        50.0, 1.15, 0.001, 0.002,
                        25.0, 0.5, 100.0, 0.0);
                    claudeTestResult = $"model={testRes.modelName} dir={testRes.direction} prob={testRes.probability} reasoning={testRes.reasoning}";
                }
                catch (Exception ex)
                {
                    claudeTestResult = "ERROR: " + ex.Message;
                }
                
                return Results.Json(new
                {
                    status = (int)response.StatusCode,
                    body = responseBody,
                    claudeTestResult = claudeTestResult,
                    claudeLastRawResponse = ClaudeSignalService.GetLastRawResponse(),
                    primaryModelError = ClaudeSignalService.GetLastPrimaryError(),
                    lastException = LastExceptionMessage
                });
            }
            catch (Exception ex)
            {
                return Results.Json(new { error = ex.Message });
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
        _ = TwelveDataWebSocketManager.StartBackgroundStreamingAsync();

        app.Run($"http://0.0.0.0:{port}");
    }

    private static async Task<(double[] prices, double[] volumes)> GetSubMinuteCandles(string? symbol, string asset, string timeframe, int limit)
    {
        string tdSymbol = TwelveDataService.ConvertToTwelveSymbol(asset) ?? asset;
        var ticks = TwelveDataWebSocketManager.GetTicks(tdSymbol);
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
                var m1Result = await FetchBinanceWithFallback(symbol, "1m", asset, fetchLimit, 10);
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

    private const int RsiPeriod = 14;
    private const int EmaShort = 9;
    private const int EmaLong = 21;

    /* ─── Cached fetch with retry ─── */

    private static async Task<(double[] prices, double[] volumes)> FetchBinanceCandles(string symbol, string interval, int limit = 50)
    {
        string url = $"https://api.binance.com/api/v3/klines?symbol={symbol}&interval={interval}&limit={limit}";
        var response = await _retryPolicy.ExecuteAsync(() => _httpClient.GetStringAsync(url));
        using var doc = JsonDocument.Parse(response);
        var arr = doc.RootElement.EnumerateArray().ToList();

        if (arr.Count > 0)
        {
            var lastCandle = arr[^1];
            long openTimeMs = lastCandle[0].GetInt64();
            var openTime = DateTimeOffset.FromUnixTimeMilliseconds(openTimeMs).UtcDateTime;

            // If data is older than 5 days, it means the symbol is delisted/inactive on Binance.
            // Throw exception so we fall back to TwelveData (for forex/commodities).
            if (DateTime.UtcNow - openTime > TimeSpan.FromDays(5))
            {
                throw new Exception($"Binance symbol {symbol} data is extremely stale ({openTime}). Symbol is likely delisted/inactive.");
            }

            bool isHighTf = interval.EndsWith("h") || interval.EndsWith("d");
            if (DateTime.UtcNow - openTime > TimeSpan.FromMinutes(30) && !isHighTf)
            {
                bool isWeekend = DateTime.UtcNow.DayOfWeek == DayOfWeek.Saturday || 
                                 DateTime.UtcNow.DayOfWeek == DayOfWeek.Sunday || 
                                 DateTime.UtcNow.DayOfWeek == DayOfWeek.Friday;
                if (isWeekend)
                {
                    Console.WriteLine($"[Weekend] Proceeding with stale weekend data for {symbol} ({openTime})");
                }
                else
                {
                    Console.WriteLine($"[Stale Data Warning] Binance symbol {symbol} has stale data from {openTime}. Proceeding anyway.");
                }
            }
        }

        var prices = arr.Select(k => double.Parse(k[4].GetString()!, CultureInfo.InvariantCulture)).ToArray();
        var volumes = arr.Select(k => double.Parse(k[5].GetString()!, CultureInfo.InvariantCulture)).ToArray();

        // Cache full OHLC for Claude pattern analysis
        var ohlc = arr.Select(k => new OhlcCandle(
            double.Parse(k[1].GetString()!, CultureInfo.InvariantCulture),
            double.Parse(k[2].GetString()!, CultureInfo.InvariantCulture),
            double.Parse(k[3].GetString()!, CultureInfo.InvariantCulture),
            double.Parse(k[4].GetString()!, CultureInfo.InvariantCulture),
            double.Parse(k[5].GetString()!, CultureInfo.InvariantCulture)
        )).ToArray();
        _ohlcCache[$"{symbol}_{interval}"] = ohlc;

        return (prices, volumes);
    }

    private static async Task<(double[] prices, double[] volumes)> FetchBinanceWithFallback(string? symbol, string interval, string? originalAsset = null, int limit = 50, int cacheTtlSeconds = 10)
    {


        if (symbol != null)
        {
            string binanceCacheKey = $"binance_raw_{symbol}_{interval}_{limit}";
            if (cacheTtlSeconds > 0 && _cache.TryGetValue(binanceCacheKey, out object? cachedVal) && cachedVal is ValueTuple<double[], double[]> cachedTuple)
            {
                return cachedTuple;
            }
        }

        // Skip Binance for forex pairs not listed on Binance (symbol == null)
        if (symbol == null)
        {
            if (originalAsset != null)
            {
                var tdResult = await TwelveDataService.FetchCandlesAsync(originalAsset, interval, limit, cacheTtlSeconds);
                if (tdResult != null)
                    return tdResult.Value;
            }
            throw new Exception($"No Binance symbol for {originalAsset}");
        }

        try
        {
            var res = await FetchBinanceCandles(symbol, interval, limit);
            if (cacheTtlSeconds > 0)
            {
                string binanceCacheKey = $"binance_raw_{symbol}_{interval}_{limit}";
                _cache.Set(binanceCacheKey, res, TimeSpan.FromSeconds(cacheTtlSeconds));
            }
            return res;
        }
        catch
        {
            // Try Twelve Data for forex pairs
            if (originalAsset != null)
            {
                var tdResult = await TwelveDataService.FetchCandlesAsync(originalAsset, interval, limit, cacheTtlSeconds);
                if (tdResult != null)
                {
                    Console.WriteLine($"[Fetch] Binance {symbol} not found, got from TwelveData ({originalAsset})");
                    return tdResult.Value;
                }
            }

            var fallback = symbol switch
            {
                "EURJPYUSDT" or "EURGBPUSDT" or "EURNZDUSDT" or "EURCHFUSDT" => "EURUSDT",
                "GBPJPYUSDT" or "GBPAUDUSDT" or "GBPCADUSDT" or "GBPCHFUSDT" => "GBPUSDT",
                "NZDJPYUSDT" or "NZDCADUSDT" or "NZDCHFUSDT" => "NZDUSDT",
                "AUDCADUSDT" or "AUDCHFUSDT" or "AUDNZDUSDT" => "AUDUSDT",
                "CADCHFUSDT" or "USDCADUSDT" or "CADJPYUSDT" => "EURUSDT",
                "USDCHFUSDT" or "CHFJPYUSDT" => "EURUSDT",
                "USDBRLUSDT" or "USDIDRUSDT" or "USDPKRUSDT" or "USDDZDUSDT" => "GBPUSDT",
                "NGNUSDUSDT" or "LBPUSDUSDT" or "TNDUSDUSDT" or "JODCNYUSDT" or "OMRCNYUSDT" or "SARCNYUSDT" => "EURUSDT",
                "BRENTUSDT" or "OILUSDT" => "EURUSDT",
                _ => null
            };

            if (fallback != null)
            {
                Console.WriteLine($"[Fetch] {symbol} not found, fallback to {fallback}");
                var res = await FetchBinanceCandles(fallback, interval, limit);
                string binanceCacheKey = $"binance_raw_{symbol}_{interval}_{limit}";
                _cache.Set(binanceCacheKey, res, TimeSpan.FromSeconds(2));
                return res;
            }

            throw;
        }
    }

    /* ─── Indicators ─── */

    private static double[] ComputeSma(double[] data, int period)
    {
        int n = data.Length;
        var sma = new double[n];
        for (int i = 0; i < n; i++)
        {
            if (i < period - 1) { sma[i] = double.NaN; continue; }
            double sum = 0;
            for (int j = i - period + 1; j <= i; j++) sum += data[j];
            sma[i] = sum / period;
        }
        return sma;
    }

    private static double ComputeEma(double[] data, int period, int index)
    {
        if (index < 0 || index >= data.Length) return double.NaN;
        double k = 2.0 / (period + 1);
        double ema = data[0];
        for (int i = 1; i <= index; i++)
            ema = data[i] * k + ema * (1 - k);
        return ema;
    }

    private static double[] ComputeEmaArray(double[] data, int period)
    {
        int n = data.Length;
        var ema = new double[n];
        double k = 2.0 / (period + 1);
        ema[0] = data[0];
        for (int i = 1; i < n; i++)
            ema[i] = data[i] * k + ema[i - 1] * (1 - k);
        return ema;
    }

    private static double[] ComputeRsiArray(double[] data, int period)
    {
        int n = data.Length;
        var rsi = new double[n];
        if (n <= period) return rsi;

        double avgGain = 0;
        double avgLoss = 0;

        // First RSI value (SMA base)
        for (int i = 1; i <= period; i++)
        {
            double diff = data[i] - data[i - 1];
            if (diff > 0) avgGain += diff; else avgLoss -= diff;
        }
        avgGain /= period;
        avgLoss /= period;

        rsi[period] = avgLoss < 1e-12 ? 100 : 100 - (100 / (1 + (avgGain / (avgLoss + 1e-12))));

        // Subsequent smoothed RSI values (Wilder's smoothing)
        for (int i = period + 1; i < n; i++)
        {
            double diff = data[i] - data[i - 1];
            double gain = diff > 0 ? diff : 0;
            double loss = diff < 0 ? -diff : 0;

            avgGain = (avgGain * (period - 1) + gain) / period;
            avgLoss = (avgLoss * (period - 1) + loss) / period;

            rsi[i] = avgLoss < 1e-12 ? 100 : 100 - (100 / (1 + (avgGain / (avgLoss + 1e-12))));
        }

        return rsi;
    }

    // Optimized: computes RSI for a single index without allocating a full array
    private static double ComputeRsi(double[] data, int period, int index)
    {
        if (data.Length <= period || index < period) return 50.0;
        int safeIndex = Math.Clamp(index, period, data.Length - 1);

        double avgGain = 0, avgLoss = 0;
        for (int i = 1; i <= period; i++)
        {
            double diff = data[i] - data[i - 1];
            if (diff > 0) avgGain += diff; else avgLoss -= diff;
        }
        avgGain /= period;
        avgLoss /= period;

        for (int i = period + 1; i <= safeIndex; i++)
        {
            double diff = data[i] - data[i - 1];
            double g = diff > 0 ? diff : 0;
            double l = diff < 0 ? -diff : 0;
            avgGain = (avgGain * (period - 1) + g) / period;
            avgLoss = (avgLoss * (period - 1) + l) / period;
        }

        return avgLoss < 1e-12 ? 100.0 : 100.0 - 100.0 / (1.0 + avgGain / (avgLoss + 1e-12));
    }

    private static (double macd, double signal) ComputeMacd(double[] data, int index)
    {
        if (data.Length < 26) return (0.0, 0.0);
        var ema12 = ComputeEmaArray(data, 12);
        var ema26 = ComputeEmaArray(data, 26);

        // Build macdHistory only up to index+9 (for signal EMA warmup)
        int len = data.Length;
        var macdHistory = new double[len];
        for (int i = 0; i < len; i++)
            macdHistory[i] = ema12[i] - ema26[i];

        var signalHistory = ComputeEmaArray(macdHistory, 9);

        int safeIndex = Math.Clamp(index, 0, len - 1);
        return (macdHistory[safeIndex], signalHistory[safeIndex]);
    }

    /* ─── Volume strength ─── */

    private static double VolumeStrength(double[] prices, double[] volumes)
    {
        int n = volumes.Length;
        if (n < 10) return 0;

        double sumVol = 0;
        for (int i = n - 10; i < n; i++)
            sumVol += volumes[i];
        double avgVol = sumVol / 10.0;
        if (avgVol < 1e-9) return 0;

        double currentVol = volumes[^1];
        double prevClose = prices[^2];
        double currentClose = prices[^1];
        double change = (currentClose - prevClose) / prevClose;

        double volRatio = currentVol / avgVol;
        double direction = change > 0 ? 1 : -1;

        double volStrength = direction * Math.Min(volRatio, 2.0) / 2.0;
        return volStrength * 2;
    }

    private static (double wt1, double wt2) ComputeWaveTrend(MiniAppController.OhlcCandle[] candles, int channelLength = 10, int averageLength = 21)
    {
        if (candles == null || candles.Length < Math.Max(channelLength, averageLength) + 5)
            return (0.0, 0.0);

        int n = candles.Length;
        double[] typicalPrices = new double[n];
        for (int i = 0; i < n; i++)
        {
            typicalPrices[i] = (candles[i].High + candles[i].Low + candles[i].Close) / 3.0;
        }

        // 1. EMA of typical price
        double[] esa = ComputeEmaArray(typicalPrices, channelLength);

        // 2. Absolute deviation
        double[] absDev = new double[n];
        for (int i = 0; i < n; i++)
        {
            absDev[i] = Math.Abs(typicalPrices[i] - esa[i]);
        }

        // 3. EMA of absolute deviation
        double[] de = ComputeEmaArray(absDev, channelLength);

        // 4. Channel Index
        double[] ci = new double[n];
        for (int i = 0; i < n; i++)
        {
            ci[i] = (typicalPrices[i] - esa[i]) / (0.015 * de[i] + 1e-10);
        }

        // 5. WaveTrend 1 (WT1) = EMA of Channel Index
        double[] wt1 = ComputeEmaArray(ci, averageLength);

        // 6. WaveTrend 2 (WT2) = 4-period SMA of WT1
        double[] wt2 = new double[n];
        for (int i = 0; i < n; i++)
        {
            int start = Math.Max(0, i - 3);
            double sum = 0;
            int count = 0;
            for (int j = start; j <= i; j++)
            {
                sum += wt1[j];
                count++;
            }
            wt2[i] = sum / count;
        }

        return (wt1[^1], wt2[^1]);
    }

    private static (string trend, double superTrendValue) ComputeSuperTrend(MiniAppController.OhlcCandle[] candles, int period = 10, double multiplier = 3.0)
    {
        if (candles == null || candles.Length < period + 5)
            return ("NEUTRAL", 0.0);

        int n = candles.Length;
        
        // Compute ATR
        double[] tr = new double[n];
        tr[0] = candles[0].High - candles[0].Low;
        for (int i = 1; i < n; i++)
        {
            double hMinusL = candles[i].High - candles[i].Low;
            double hMinusPc = Math.Abs(candles[i].High - candles[i - 1].Close);
            double lMinusPc = Math.Abs(candles[i].Low - candles[i - 1].Close);
            tr[i] = Math.Max(hMinusL, Math.Max(hMinusPc, lMinusPc));
        }

        double[] atr = ComputeEmaArray(tr, period);

        double[] finalUpperBand = new double[n];
        double[] finalLowerBand = new double[n];
        bool[] isBullish = new bool[n];
        double[] superTrend = new double[n];

        double basicUpper = (candles[0].High + candles[0].Low) / 2.0 + multiplier * atr[0];
        double basicLower = (candles[0].High + candles[0].Low) / 2.0 - multiplier * atr[0];
        finalUpperBand[0] = basicUpper;
        finalLowerBand[0] = basicLower;
        isBullish[0] = candles[0].Close > basicUpper;
        superTrend[0] = isBullish[0] ? basicLower : basicUpper;

        for (int i = 1; i < n; i++)
        {
            double median = (candles[i].High + candles[i].Low) / 2.0;
            basicUpper = median + multiplier * atr[i];
            basicLower = median - multiplier * atr[i];

            finalUpperBand[i] = (basicUpper < finalUpperBand[i - 1] || candles[i - 1].Close > finalUpperBand[i - 1]) ? basicUpper : finalUpperBand[i - 1];
            finalLowerBand[i] = (basicLower > finalLowerBand[i - 1] || candles[i - 1].Close < finalLowerBand[i - 1]) ? basicLower : finalLowerBand[i - 1];

            isBullish[i] = isBullish[i - 1];
            if (candles[i].Close > finalUpperBand[i])
                isBullish[i] = true;
            else if (candles[i].Close < finalLowerBand[i])
                isBullish[i] = false;

            superTrend[i] = isBullish[i] ? finalLowerBand[i] : finalUpperBand[i];
        }

        return (isBullish[^1] ? "BULLISH" : "BEARISH", superTrend[^1]);
    }

    private static double AnalyzeVolumeSpread(MiniAppController.OhlcCandle[] candles)
    {
        if (candles == null || candles.Length < 10) return 0.0;

        int last = candles.Length - 1;
        double spread = candles[last].High - candles[last].Low;

        // Check if we need to estimate volumes (for Forex on weekdays or missing feeds)
        bool estimateVolume = candles.Average(c => c.Volume) < 0.01;
        double[] finalVolumes = new double[candles.Length];
        
        if (estimateVolume)
        {
            // Estimate proxy volume based on candle spread relative to average spread
            double[] windowSpreads = candles.Select(c => c.High - c.Low).ToArray();
            double avgSpreadAll = windowSpreads.Average();
            for (int i = 0; i < candles.Length; i++)
            {
                double s = candles[i].High - candles[i].Low;
                double ratio = s / (avgSpreadAll + 1e-12);
                finalVolumes[i] = 100.0 * ratio + 10.0; // base activity of 10 ticks
            }
        }
        else
        {
            for (int i = 0; i < candles.Length; i++)
            {
                finalVolumes[i] = candles[i].Volume;
            }
        }

        double volume = finalVolumes[last];

        // Compute average spread and volume for context
        double[] spreads = candles.Skip(candles.Length - 10).Take(10).Select(c => c.High - c.Low).ToArray();
        double[] volumes = finalVolumes.Skip(finalVolumes.Length - 10).Take(10).ToArray();
        
        double avgSpread = spreads.Average();
        double avgVolume = volumes.Average();

        if (avgSpread < 1e-10 || avgVolume < 1e-10) return 0.0;

        double spreadRatio = spread / avgSpread;
        double volumeRatio = volume / avgVolume;

        // Volume Spread Analysis (VSA)
        if (candles[last].Close > candles[last].Open)
        {
            // High Volume + High Spread -> Strong Bullish Continuation
            if (volumeRatio > 1.3 && spreadRatio > 1.3) return 0.4;
            // High Volume + Tiny Spread -> Absorption / Exhaustion (Bearish Reversal Risk)
            if (volumeRatio > 1.4 && spreadRatio < 0.7) return -0.4;
            // Low Volume + High Spread -> Fake Bullish Breakout
            if (volumeRatio < 0.7 && spreadRatio > 1.3) return -0.3;
        }
        else
        {
            // High Volume + High Spread -> Strong Bearish Continuation
            if (volumeRatio > 1.3 && spreadRatio > 1.3) return -0.4;
            // High Volume + Tiny Spread -> Absorption / Exhaustion (Bullish Reversal Risk)
            if (volumeRatio > 1.4 && spreadRatio < 0.7) return 0.4;
            // Low Volume + High Spread -> Fake Bearish Breakout
            if (volumeRatio < 0.7 && spreadRatio > 1.3) return 0.3;
        }

        return 0.0;
    }

    private static double GetFibonacciBounce(double[] prices)
    {
        if (prices == null || prices.Length < 30) return 0.0;

        int len = Math.Min(45, prices.Length);
        var recentPrices = prices[^len..];
        double swingHigh = recentPrices.Max();
        double swingLow = recentPrices.Min();
        double range = swingHigh - swingLow;

        if (range < 1e-10) return 0.0;

        double currentPrice = prices[^1];
        bool generalTrendUp = prices[^1] > recentPrices[0];

        // Fibonacci Retracement Levels
        double fib618 = generalTrendUp ? swingHigh - 0.618 * range : swingLow + 0.618 * range;
        double fib50 = generalTrendUp ? swingHigh - 0.5 * range : swingLow + 0.5 * range;
        double fib382 = generalTrendUp ? swingHigh - 0.382 * range : swingLow + 0.382 * range;

        double tolerance = 0.02 * range;

        if (generalTrendUp)
        {
            if (Math.Abs(currentPrice - fib618) < tolerance) return 0.35;
            if (Math.Abs(currentPrice - fib50) < tolerance) return 0.25;
            if (Math.Abs(currentPrice - fib382) < tolerance) return 0.15;
        }
        else
        {
            if (Math.Abs(currentPrice - fib618) < tolerance) return -0.35;
            if (Math.Abs(currentPrice - fib50) < tolerance) return -0.25;
            if (Math.Abs(currentPrice - fib382) < tolerance) return -0.15;
        }

        return 0.0;
    }

    /* ─── True ADX (Wilders) & ATR ─── */

    private static (double adx, double plusDi, double minusDi) ComputeTrueAdx(MiniAppController.OhlcCandle[] candles, int period = 14)
    {
        if (candles == null || candles.Length < period * 2) return (20, 0, 0);

        int n = candles.Length;
        var tr = new double[n];
        var plusDm = new double[n];
        var minusDm = new double[n];

        for (int i = 1; i < n; i++)
        {
            double h = candles[i].High, l = candles[i].Low;
            double pc = candles[i - 1].Close, ph = candles[i - 1].High, pl = candles[i - 1].Low;

            tr[i] = Math.Max(h - l, Math.Max(Math.Abs(h - pc), Math.Abs(l - pc)));

            double up = h - ph, down = pl - l;
            if (up > down && up > 0) plusDm[i] = up;
            if (down > up && down > 0) minusDm[i] = down;
        }

        // Wilder's smoothing: first SMA(period), then EMA with alpha = 1/period
        var sTr = new double[n]; var sP = new double[n]; var sM = new double[n];
        for (int i = 1; i <= period; i++) { sTr[period] += tr[i]; sP[period] += plusDm[i]; sM[period] += minusDm[i]; }
        sTr[period] /= period; sP[period] /= period; sM[period] /= period;

        for (int i = period + 1; i < n; i++)
        {
            sTr[i] = sTr[i - 1] * (period - 1) / period + tr[i] / period;
            sP[i] = sP[i - 1] * (period - 1) / period + plusDm[i] / period;
            sM[i] = sM[i - 1] * (period - 1) / period + minusDm[i] / period;
        }

        // +DI, -DI, DX
        var dx = new double[n];
        double finalPdi = 0, finalMdi = 0;
        for (int i = period; i < n; i++)
        {
            if (sTr[i] > 1e-10)
            {
                double pdi = 100 * sP[i] / sTr[i], mdi = 100 * sM[i] / sTr[i];
                if (i == n - 1) { finalPdi = pdi; finalMdi = mdi; }
                double sum = pdi + mdi;
                if (sum > 1e-10) dx[i] = 100 * Math.Abs(pdi - mdi) / sum;
            }
        }

        // ADX = smoothed DX
        double adx = 0;
        int validDx = 0;
        for (int i = period; i < Math.Min(period * 2, n); i++)
        {
            if (dx[i] > 0) { adx += dx[i]; validDx++; }
        }
        if (validDx == 0) return (20, finalPdi, finalMdi);
        adx /= validDx;

        for (int i = period * 2; i < n; i++)
        {
            if (dx[i] > 0) adx = (adx * (period - 1) + dx[i]) / period;
        }

        return (adx, finalPdi, finalMdi);
    }

    private static double ComputeAtr(MiniAppController.OhlcCandle[] candles, int period = 14)
    {
        if (candles == null || candles.Length < period + 1) return 0;

        int n = candles.Length;
        double atr = 0;
        // First ATR = SMA of TR over `period`
        for (int i = 1; i <= period; i++)
        {
            double h = candles[i].High, l = candles[i].Low, pc = candles[i - 1].Close;
            atr += Math.Max(h - l, Math.Max(Math.Abs(h - pc), Math.Abs(l - pc)));
        }
        atr /= period;

        // Wilder's smoothing
        for (int i = period + 1; i < n; i++)
        {
            double h = candles[i].High, l = candles[i].Low, pc = candles[i - 1].Close;
            double tr = Math.Max(h - l, Math.Max(Math.Abs(h - pc), Math.Abs(l - pc)));
            atr = (atr * (period - 1) + tr) / period;
        }

        return atr;
    }

    private static double CalculateVolatilityRatio(double[] prices)
    {
        int n = prices.Length;
        if (n < 20) return 1.0;

        var absoluteChanges = new List<double>();
        for (int i = 1; i < n; i++)
        {
            absoluteChanges.Add(Math.Abs(prices[i] - prices[i - 1]));
        }

        if (absoluteChanges.Count < 10) return 1.0;

        double currentVolatility = absoluteChanges.TakeLast(5).Average();
        double historicalVolatility = absoluteChanges.Average();

        if (historicalVolatility < 1e-10) return 1.0;

        return currentVolatility / historicalVolatility;
    }

    /* ─── Bollinger z-score ─── */

    private static double ComputeBollingerZscore(double[] data, int period)
    {
        if (data.Length < period) return 0;
        var window = data.TakeLast(period).ToArray();
        double mean = window.Average();
        double variance = window.Sum(v => Math.Pow(v - mean, 2)) / period;
        double std = Math.Sqrt(variance);
        if (std < 1e-10) return 0;
        return (data[^1] - mean) / std;
    }

    /* ─── RSI divergence ─── */

    private static (bool bullish, bool bearish) DetectRsiDivergence(double[] data, int period)
    {
        int n = data.Length;
        if (n < period * 2 + 5) return (false, false);
        int mid = n - period;
        double priceMin1 = data.Skip(mid - period).Take(period).Min();
        double priceMax1 = data.Skip(mid - period).Take(period).Max();
        double priceMin2 = data.Skip(n - period).Take(period).Min();
        double priceMax2 = data.Skip(n - period).Take(period).Max();
        double rsi1 = ComputeRsi(data, period, mid);
        double rsi2 = ComputeRsi(data, period, n - 1);
        bool bullish = priceMin2 < priceMin1 && rsi2 > rsi1 + 5;
        bool bearish = priceMax2 > priceMax1 && rsi2 < rsi1 - 5;
        return (bullish, bearish);
    }

    /* ─── Linear regression slope ─── */

    private static double LinearRegressionSlope(double[] data, int len)
    {
        int n = Math.Min(len, data.Length);
        if (n < 3) return 0;
        var segment = data.TakeLast(n).ToArray();
        double sumX = 0, sumY = 0, sumXY = 0, sumX2 = 0;
        for (int i = 0; i < n; i++)
        {
            sumX += i; sumY += segment[i]; sumXY += i * segment[i]; sumX2 += i * i;
        }
        double slope = (n * sumXY - sumX * sumY) / (n * sumX2 - sumX * sumX);
        return slope / (segment.Average() + 1e-10) * 100;
    }

    /* ─── Scoring Engine ─── */

    private static (double score, double confidence, double rsiVal, double emaVal, double volStrengthVal, double atrVal)
        ScoreTimeframe(double[] prices, double[] volumes, OhlcCandle[]? candles = null, double? adxOverride = null, double? atrOverride = null, bool isForex = false)
    {
        int n = prices.Length;
        if (n < EmaLong + 5) return (0, 50, 50, 0, 0, 0);

        var emaShortArr = ComputeEmaArray(prices, EmaShort);
        var emaLongArr = ComputeEmaArray(prices, EmaLong);
        double rsi = ComputeRsi(prices, RsiPeriod, n - 1);
        var (macd, signal) = ComputeMacd(prices, n - 1);
        double lastPrice = prices[^1];
        double emaS = emaShortArr[^1];
        double emaL = emaLongArr[^1];
        double atr = atrOverride ?? 0;
        double adx = adxOverride ?? 20.0;

        double score = 0;

        // --- 0. Hurst Exponent Regime Weights ---
        double hurst = CalculateHurstExponent(prices);
        double trendWeight = 1.0;
        double rangeWeight = 1.0;

        if (hurst < 0.45)
        {
            // Mean-reverting regime: suppress trend-following, boost oscillators
            trendWeight = 0.55;
            rangeWeight = 1.45;
        }
        else if (hurst > 0.55)
        {
            // Trending regime: boost trend-following, suppress oscillators
            trendWeight = 1.45;
            rangeWeight = 0.55;
        }
        else
        {
            // Random Walk / Balanced regime: default to ADX scaling
            if (adx > 25.0)
            {
                trendWeight = adx > 40.0 ? 1.4 : 1.15;
                rangeWeight = adx > 40.0 ? 0.35 : 0.55;
            }
            else if (adx < 20.0)
            {
                trendWeight = adx < 15.0 ? 0.45 : 0.65;
                rangeWeight = adx < 15.0 ? 1.35 : 1.15;
            }
        }

        // Apply Forex Session weight multipliers
        var (trendAdj, rangeAdj, sessionName) = GetSessionMultipliers(isForex);
        trendWeight *= trendAdj;
        rangeWeight *= rangeAdj;
        Console.WriteLine($"[Regime-Consensus] Hurst={hurst:F2}, Session={sessionName} (TrendWeight={trendWeight:F2}, RangeWeight={rangeWeight:F2})");

        // 1. Trend Direction (EMA 9 vs 21) — proportional (no dead zone needed)
        double emaSpread = (emaS - emaL) / (lastPrice + 1e-10) * 10000; // basis points
        score += Math.Clamp(emaSpread / 5.0, -1.0, 1.0) * trendWeight;

        // 2. Momentum (MACD vs Signal) — proportional (no dead zone needed)
        double macdDiff = (macd - signal) / (lastPrice + 1e-10) * 10000; // basis points
        score += Math.Clamp(macdDiff / 3.0, -1.0, 1.0) * trendWeight;

        // 3. Acceleration (ROC 3 and 5) — raised threshold to filter 1-min noise
        double mom3 = prices.Length >= 4 ? (prices[^1] - prices[^4]) / prices[^4] * 100 : 0;
        double mom5 = prices.Length >= 6 ? (prices[^1] - prices[^6]) / prices[^6] * 100 : 0;
        double roccScore = 0;
        if (mom3 > 0.02) roccScore += 0.5; else if (mom3 < -0.02) roccScore -= 0.5;
        if (mom5 > 0.02) roccScore += 0.5; else if (mom5 < -0.02) roccScore -= 0.5;
        score += roccScore * trendWeight;

        // 4. RSI — active mean-reversion signal with adaptive volatility boundaries
        double volatilityRatio = CalculateVolatilityRatio(prices);
        double rsiObLimit = Math.Clamp(50.0 + 15.0 * volatilityRatio, 62.0, 82.0);
        double rsiOsLimit = Math.Clamp(50.0 - 15.0 * volatilityRatio, 18.0, 38.0);
        double rsiScore = 0;
        if (rsi > rsiObLimit)
            rsiScore = -Math.Clamp((rsi - 50.0) / (rsiObLimit - 50.0), 0.0, 1.25);
        else if (rsi < rsiOsLimit)
            rsiScore = Math.Clamp((50.0 - rsi) / (50.0 - rsiOsLimit), 0.0, 1.25);
        score += rsiScore * rangeWeight;

        // 5. WaveTrend Oscillator (Advanced Range / Cycle oscillator)
        if (candles != null)
        {
            var (wt1, wt2) = ComputeWaveTrend(candles);
            double wtDiff = wt1 - wt2;
            double waveTrendScore = 0;

            // Exhaustion zones WT1 > 50 or < -50
            if (wt1 > 50.0) waveTrendScore -= Math.Clamp((wt1 - 30.0) / 30.0, 0.0, 1.3);
            else if (wt1 < -50.0) waveTrendScore += Math.Clamp((-30.0 - wt1) / 30.0, 0.0, 1.3);

            // Cross over/under momentum
            waveTrendScore += Math.Clamp(wtDiff / 10.0, -0.8, 0.8);
            score += waveTrendScore * rangeWeight;
        }

        // 6. SuperTrend Trailing Stop
        if (candles != null)
        {
            var (stTrend, _) = ComputeSuperTrend(candles);
            if (stTrend == "BULLISH") score += 0.5 * trendWeight;
            else if (stTrend == "BEARISH") score -= 0.5 * trendWeight;
        }

        // 7. Volume Spread Analysis (VSA)
        if (candles != null)
        {
            score += AnalyzeVolumeSpread(candles);
        }

        // 8. Fibonacci golden ratio bounce
        score += GetFibonacciBounce(prices);

        // 9. Kalman Filter Price Trend & Dev (Lag-free estimate)
        var kalman = ComputeKalmanFilter(prices);
        if (kalman.Length >= 2)
        {
            double kalmanSlope = (kalman[^1] - kalman[^2]) / (kalman[^2] + 1e-10) * 10000; // basis points slope
            double priceDevBps = (prices[^1] - kalman[^1]) / (kalman[^1] + 1e-10) * 10000; // basis points price dev

            double kalmanScore = 0;
            // Trend-following component (slope)
            if (kalmanSlope > 0.1) kalmanScore += 0.45 * trendWeight;
            else if (kalmanSlope < -0.1) kalmanScore -= 0.45 * trendWeight;

            // Mean-reversion component (price deviation from true value)
            // If price deviates significantly from its Kalman line, expect pullback
            double devThresh = prices[0] > 100 ? 25.0 : 8.0; // 25 bps for crypto/stocks, 8 bps for forex
            if (priceDevBps > devThresh) kalmanScore -= 0.4 * rangeWeight;
            else if (priceDevBps < -devThresh) kalmanScore += 0.4 * rangeWeight;

            score += kalmanScore;
            Console.WriteLine($"[Kalman-Filter] Slope={kalmanSlope:F2}bps, Dev={priceDevBps:F2}bps -> score contribution: {kalmanScore:F2}");
        }

        // 10. TD Sequential trend exhaustion count (Setup 9 - 13)
        score += ComputeDeMarkScore(prices) * rangeWeight;

        // 11. Linear Regression Channel (LRC) deviation score
        double lrcZscore = CalculateLrcZscore(prices, 20);
        if (Math.Abs(lrcZscore) > 1.5)
        {
            double lrcScore = 0;
            if (lrcZscore > 1.5)
                lrcScore = -Math.Clamp((lrcZscore - 1.5) * 0.8, 0.0, 0.8);
            else
                lrcScore = Math.Clamp((-1.5 - lrcZscore) * 0.8, 0.0, 0.8);
            
            score += lrcScore * rangeWeight;
            Console.WriteLine($"[LRC-Channel] Z-score={lrcZscore:F2} -> score contribution: {lrcScore * rangeWeight:F2}");
        }

        double volStrength = 0.0;
        if (volumes != null && volumes.Length >= 5)
        {
            double sumVol = 0;
            int count = Math.Min(volumes.Length - 1, 10);
            for (int i = volumes.Length - 1 - count; i < volumes.Length - 1; i++)
            {
                sumVol += volumes[i];
            }
            double avgVol = count > 0 ? sumVol / count : 1.0;
            double currentVol = volumes[^1];
            double ratio = avgVol > 0 ? currentVol / avgVol : 1.0;
            double priceChange = prices.Length >= 2 ? prices[^1] - prices[^2] : 0;
            volStrength = (priceChange >= 0 ? 1.0 : -1.0) * ratio;
        }
        else if (candles != null && candles.Length >= 5)
        {
            double sumVol = 0;
            int count = Math.Min(candles.Length - 1, 10);
            for (int i = candles.Length - 1 - count; i < candles.Length - 1; i++)
            {
                sumVol += candles[i].Volume;
            }
            double avgVol = count > 0 ? sumVol / count : 1.0;
            double currentVol = candles[^1].Volume;
            double ratio = avgVol > 0 ? currentVol / avgVol : 1.0;
            double priceChange = candles[^1].Close - candles[^1].Open;
            volStrength = (priceChange >= 0 ? 1.0 : -1.0) * ratio;
        }

        double confidence = 50;
        double absScore = Math.Abs(score);
        if (absScore >= 3.0) confidence = 92;
        else if (absScore >= 1.8) confidence = 78;
        else confidence = 50;

        return (score, confidence, rsi, emaS, volStrength, atr);
    }




    public static string SanitizeAsset(string raw)
    {
        return AssetSanitizer.Sanitize(raw);
    }

    private static double CalculateHurstExponent(double[] prices)
    {
        int n = prices.Length;
        if (n < 30) return 0.5;

        // Calculate differences at scale 2 (2-bar changes)
        var diff2 = new List<double>();
        for (int i = 2; i < n; i += 2)
        {
            diff2.Add(prices[i] - prices[i - 2]);
        }

        // Calculate differences at scale 16 (16-bar changes)
        var diff16 = new List<double>();
        for (int i = 16; i < n; i += 16)
        {
            diff16.Add(prices[i] - prices[i - 16]);
        }

        if (diff2.Count < 4 || diff16.Count < 2) return 0.5;

        double mean2 = diff2.Average();
        double var2 = diff2.Sum(d => Math.Pow(d - mean2, 2)) / diff2.Count;
        double std2 = Math.Sqrt(var2);

        double mean16 = diff16.Average();
        double var16 = diff16.Sum(d => Math.Pow(d - mean16, 2)) / diff16.Count;
        double std16 = Math.Sqrt(var16);

        if (std2 < 1e-12) return 0.5;

        // H = log(std16 / std2) / log(16 / 2)
        // since log(16/2) = log(8)
        double ratio = std16 / std2;
        if (ratio < 1e-10) return 0.0;
        
        double hurst = Math.Log(ratio) / Math.Log(8.0);
        return Math.Clamp(hurst, 0.0, 1.0);
    }

    private static double[] ComputeKalmanFilter(double[] prices)
    {
        int n = prices.Length;
        var filtered = new double[n];
        if (n == 0) return filtered;

        // Calculate standard deviation of prices to set R and Q dynamically
        double mean = prices.Average();
        double variance = prices.Sum(p => Math.Pow(p - mean, 2)) / n;
        double std = Math.Sqrt(variance);

        double R = std * std;
        if (R < 1e-10) R = 1e-4;
        double Q = R * 0.02; // Process variance is 2% of measurement noise

        double x = prices[0]; // initial state estimate
        double P = 1.0;       // initial estimation error covariance

        filtered[0] = x;

        for (int i = 1; i < n; i++)
        {
            // Predict
            P = P + Q;

            // Correct
            double K = P / (P + R);
            x = x + K * (prices[i] - x);
            P = (1 - K) * P;

            filtered[i] = x;
        }

        return filtered;
    }

    private static double ComputeDeMarkScore(double[] prices)
    {
        int n = prices.Length;
        if (n < 13) return 0.0;

        int currentBuySetup = 0;
        int currentSellSetup = 0;

        for (int i = 4; i < n; i++)
        {
            if (prices[i] < prices[i - 4])
            {
                currentBuySetup++;
                currentSellSetup = 0;
            }
            else if (prices[i] > prices[i - 4])
            {
                currentSellSetup++;
                currentBuySetup = 0;
            }
            else
            {
                currentBuySetup = 0;
                currentSellSetup = 0;
            }
        }

        // Setup completions (9 through 13 represent mature exhaustion zones)
        if (currentBuySetup >= 9)
        {
            Console.WriteLine($"[TD-Sequential] TD Buy Setup {currentBuySetup} detected (Trend exhausted DOWN -> expecting UP).");
            return 0.35;
        }
        if (currentSellSetup >= 9)
        {
            Console.WriteLine($"[TD-Sequential] TD Sell Setup {currentSellSetup} detected (Trend exhausted UP -> expecting DOWN).");
            return -0.35;
        }

        return 0.0;
    }

    private static (double trendAdj, double rangeAdj, string sessionName) GetSessionMultipliers(bool isForex)
    {
        // Forex sessions only apply on weekdays to Forex pairs
        if (!isForex) return (1.0, 1.0, "CRYPTO / 24/7");

        DayOfWeek day = DateTime.UtcNow.DayOfWeek;
        bool isWeekend = day == DayOfWeek.Saturday || day == DayOfWeek.Sunday;
        if (isWeekend) return (1.0, 1.0, "FOREX WEEKEND (SYNTHETIC)");

        int hour = DateTime.UtcNow.Hour;
        
        // Asian Session (22:00 - 07:00 UTC)
        if (hour >= 22 || hour < 7)
        {
            // Low volatility, range-bound mean reversion is favored
            return (0.75, 1.25, "ASIAN (RANGE)");
        }
        // London/NY Overlap (12:00 - 16:00 UTC)
        else if (hour >= 12 && hour < 16)
        {
            // Maximum volatility and trending breakouts
            return (1.30, 0.70, "LONDON-NY OVERLAP (TREND)");
        }
        // European Session (07:00 - 12:00 UTC)
        else if (hour >= 7 && hour < 12)
        {
            // Trending behavior favored
            return (1.15, 0.85, "LONDON (TREND)");
        }
        // Late US Session (16:00 - 22:00 UTC)
        else
        {
            // Balanced but still trending biased
            return (1.10, 0.90, "NEW YORK (BALANCED)");
        }
    }

    private static double CalculateLrcZscore(double[] prices, int len)
    {
        int n = Math.Min(len, prices.Length);
        if (n < 5) return 0.0;

        var segment = prices.TakeLast(n).ToArray();
        
        // Fit linear regression y = slope * x + intercept
        double sumX = 0, sumY = 0, sumXY = 0, sumX2 = 0;
        for (int i = 0; i < n; i++)
        {
            sumX += i;
            sumY += segment[i];
            sumXY += i * segment[i];
            sumX2 += i * i;
        }
        double denominator = n * sumX2 - sumX * sumX;
        if (Math.Abs(denominator) < 1e-12) return 0.0;

        double slope = (n * sumXY - sumX * sumY) / denominator;
        double intercept = (sumY - slope * sumX) / n;

        // Calculate standard deviation of residuals (distances from the regression line)
        double sumSqResiduals = 0;
        for (int i = 0; i < n; i++)
        {
            double expected = slope * i + intercept;
            double residual = segment[i] - expected;
            sumSqResiduals += residual * residual;
        }
        double stdDev = Math.Sqrt(sumSqResiduals / n);
        if (stdDev < 1e-12) return 0.0;

        // Z-score for the last price
        double lastExpected = slope * (n - 1) + intercept;
        double lastResidual = prices[^1] - lastExpected;

        return lastResidual / stdDev;
    }

    /* ─── Fallback ─── */

    private static object GetMomentumPrediction(string asset, string tf)
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

    private static readonly ConcurrentDictionary<long, DateTime> UserLastRequestTime = new();

    private static bool IsRequestAuthorized(HttpContext context, out string? errorMessage)
    {
        errorMessage = null;

        string? botToken = TelegramNotifier.GetToken();
        if (string.IsNullOrEmpty(botToken))
        {
            return true; // Bypass validation in local dev environment
        }

        if (!context.Request.Headers.TryGetValue("X-Telegram-Init-Data", out var initDataValues))
        {
            errorMessage = "Missing authorization header";
            return false;
        }

        string initData = initDataValues.ToString();
        if (string.IsNullOrEmpty(initData))
        {
            errorMessage = "Empty authorization token";
            return false;
        }

        // ─── Custom Signed URL Validation ───
        if (initData.Contains("custom_user_id=") && initData.Contains("custom_user_sign="))
        {
            var query = HttpUtility.ParseQueryString(initData);
            string? customIdStr = query["custom_user_id"];
            string? customSign = query["custom_user_sign"];

            if (long.TryParse(customIdStr, out long userId))
            {
                using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(botToken));
                byte[] hashBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(userId.ToString()));
                string expectedSign = Convert.ToHexString(hashBytes).ToLowerInvariant();

                if (string.Equals(customSign, expectedSign, StringComparison.OrdinalIgnoreCase))
                {
                    if (!TelegramBotService.IsUserAllowed(userId))
                    {
                        errorMessage = "Access Denied: Pocket Option registration and deposit required";
                        return false;
                    }
                    context.Items["userId"] = userId;
                    return true;
                }
            }

            errorMessage = "Invalid custom authorization signature";
            return false;
        }

        // ─── Standard Telegram InitData Validation ───
        if (!TelegramInitDataValidator.Validate(initData, botToken, out long tgUserId, out _))
        {
            errorMessage = "Invalid Telegram authorization signature";
            return false;
        }

        if (!TelegramBotService.IsUserAllowed(tgUserId))
        {
            errorMessage = "Access Denied: Pocket Option registration and deposit required";
            return false;
        }

        context.Items["userId"] = tgUserId;
        return true;
    }

    private static bool IsRateLimited(HttpContext context, out string? errorMessage)
    {
        errorMessage = null;
        if (context.Items.TryGetValue("userId", out var obj) && obj is long userId && userId > 0)
        {
            DateTime now = DateTime.UtcNow;
            if (UserLastRequestTime.TryGetValue(userId, out DateTime lastTime))
            {
                double secondsSince = (now - lastTime).TotalSeconds;
                if (secondsSince < 4) // 4 seconds rate limit
                {
                    errorMessage = $"Too many requests. Please wait {Math.Ceiling(4 - secondsSince)}s.";
                    return true;
                }
            }
            UserLastRequestTime[userId] = now;
        }
        return false;
    }
}
