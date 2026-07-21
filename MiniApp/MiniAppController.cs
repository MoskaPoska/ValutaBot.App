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

public static class MiniAppController
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

    /* ─── Multi-TF conflict penalty ─── */

    private static double MfConflictPenalty((double score, double conf, double rsi, double ema, double vol, double atr) main,
                                             (double score, double conf, double rsi, double ema, double vol, double atr) higher)
    {
        // If main and higher TF disagree → reduce confidence
        int mainDir = main.score >= 0 ? 1 : -1;
        int higherDir = higher.score >= 0 ? 1 : -1;
        if (mainDir != higherDir)
            return 0.7; // 30% penalty
        return 1.0;
    }

    /* ─── Main analysis ─── */

    internal static async Task<object> ExecuteBinanceAnalysis(string asset, string timeframe)
    {
        try
        {
            string clean = SanitizeAsset(asset);
            DayOfWeek day = DateTime.UtcNow.DayOfWeek;
            bool isWeekend = day == DayOfWeek.Saturday || day == DayOfWeek.Sunday;

            string? symbol = isWeekend switch
            {
                true => clean switch
                {
                    "BTCUSDT" or "BTC" or "BTCUSD" => "BTCUSDT",
                    "ETHUSDT" or "ETH" or "ETHUSD" => "ETHUSDT",
                    "SOLUSDT" or "SOL" or "SOLUSD" => "SOLUSDT",
                    "EURUSD" or "EURUSDT" => "EURUSDT",
                    "GBPUSD" or "GBPUSDT" => "GBPUSDT",
                    "AUDUSD" or "AUDUSDT" => "AUDUSDT",
                    _ => null
                },
                false => null // Weekdays: 100% TwelveData for all assets
            };

            bool isForex = symbol == null || symbol == "EURUSDT" || symbol == "GBPUSDT" || symbol == "AUDUSDT";
            bool isMajor = symbol == "BTCUSDT" || symbol == "ETHUSDT" || symbol == "SOLUSDT";
            int limit = 100;
            string tfLower = timeframe.ToLower().Trim();
            if (tfLower == "s10" || tfLower == "s15" || tfLower == "s30")
            {
                limit = 130; // Slightly more history to stabilize levels on fast timeframes
            }
            else if (tfLower == "m1" || tfLower == "m2" || tfLower == "m3" || tfLower == "m5")
            {
                limit = 150; // Extra history to detect strong support/resistance zones
            }
            else if (tfLower == "m15" || tfLower == "m30" || tfLower == "h1")
            {
                limit = 200; // Deep historical trend context
            }

            // Enable multi-timeframe for all assets (leveraging the 60-second TwelveData cache to prevent rate-limit depletion)
            bool useMultiTf = true;

            string mainInterval = IntervalMap(timeframe);
            string? higherTf = useMultiTf ? HigherTf(timeframe) : null;
            string? lowerTf = useMultiTf ? LowerTf(timeframe) : null;

            // Helper function to safely fetch other timeframes without failing the entire analysis
            async Task<(double[] prices, double[] volumes)?> SafeFetch(string tf)
            {
                try
                {
                    return await FetchBinanceWithFallback(symbol, tf, asset, limit);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Fetch Warning] TF {tf} failed to fetch: {ex.Message}");
                    return null;
                }
            }

            double[] mainPrices;
            double[] mainVolumes;

            if (timeframe.ToLower().StartsWith("s"))
            {
                var subMinuteResult = await GetSubMinuteCandles(symbol, clean, timeframe, limit);
                mainPrices = subMinuteResult.prices;
                mainVolumes = subMinuteResult.volumes;
            }
            else
            {
                int mainCacheTtl = 10;
                var mainResultTuple = await FetchBinanceWithFallback(symbol, mainInterval, clean, limit, mainCacheTtl);
                mainPrices = mainResultTuple.prices;
                mainVolumes = mainResultTuple.volumes;
            }

            // Check for flat/closed market (all prices identical or static)
            if (mainPrices != null && mainPrices.Length >= 15)
            {
                bool isFlat = true;
                double first = mainPrices[^1];
                for (int i = 2; i <= 15; i++)
                {
                    if (Math.Abs(mainPrices[^i] - first) > 1e-7)
                    {
                        isFlat = false;
                        break;
                    }
                }
                if (isFlat)
                {
                    throw new Exception("Market data is completely flat/closed.");
                }
            }

            // Fetch higher/lower timeframes safely
            Task<(double[] prices, double[] volumes)?>? higherTask = higherTf != null ? SafeFetch(IntervalMap(higherTf)) : null;
            Task<(double[] prices, double[] volumes)?>? lowerTask = lowerTf != null ? SafeFetch(IntervalMap(lowerTf)) : null;

            // Fetch extra timeframes safely for major pairs
            var extraTasks = new List<(string tf, Task<(double[] prices, double[] volumes)?> task)>();
            if (isMajor && useMultiTf && !isForex)
            {
                string[] allUniqueTfs = ["1m", "3m", "5m", "15m", "30m", "1h", "4h"];
                var fetchedIntervals = new HashSet<string> { mainInterval };
                if (higherTf != null) fetchedIntervals.Add(IntervalMap(higherTf));
                if (lowerTf != null) fetchedIntervals.Add(IntervalMap(lowerTf));

                foreach (var tf in allUniqueTfs)
                {
                    if (!fetchedIntervals.Contains(tf))
                    {
                        extraTasks.Add((tf, SafeFetch(tf)));
                    }
                }
            }

            var higherResultData = higherTask != null ? await higherTask : null;
            var lowerResultData = lowerTask != null ? await lowerTask : null;

            var extraResults = new List<(double[] prices, double[] volumes)>();
            foreach (var et in extraTasks)
            {
                var res = await et.task;
                if (res != null)
                {
                    extraResults.Add(res.Value);
                }
            }

            // ─── Resolve OHLC keys and patch with live WebSockets price before analysis ───
            string mainOhlcKey = timeframe.ToLower().StartsWith("s")
                ? (symbol != null ? $"{symbol}_{timeframe.ToLower()}" : $"{asset}_{timeframe.ToLower()}")
                : (symbol != null ? $"{symbol}_{mainInterval}" : $"{asset}_{mainInterval}");
            string? higherOhlcKey = higherTf != null
                ? (symbol != null ? $"{symbol}_{IntervalMap(higherTf)}" : $"{asset}_{IntervalMap(higherTf)}") : null;
            string? lowerOhlcKey = lowerTf != null
                ? (symbol != null ? $"{symbol}_{IntervalMap(lowerTf)}" : $"{asset}_{IntervalMap(lowerTf)}") : null;

            var mainOhlc = GetOhlcCandles(mainOhlcKey);
            var higherOhlc = higherOhlcKey != null ? GetOhlcCandles(higherOhlcKey) : null;
            var lowerOhlc = lowerOhlcKey != null ? GetOhlcCandles(lowerOhlcKey) : null;

            if (symbol == null)
            {
                string tdSymbol = TwelveDataService.ConvertToTwelveSymbol(asset) ?? asset;
                double lastWsPrice = TwelveDataWebSocketManager.GetLastPrice(tdSymbol);
                if (lastWsPrice > 0)
                {
                    if (mainPrices != null && mainPrices.Length > 0)
                    {
                        mainPrices = (double[])mainPrices.Clone();
                        mainPrices[^1] = lastWsPrice;
                    }
                    if (mainOhlc != null && mainOhlc.Length > 0)
                    {
                        mainOhlc = (OhlcCandle[])mainOhlc.Clone();
                        var lastCandle = mainOhlc[^1];
                        mainOhlc[^1] = new OhlcCandle(
                            lastCandle.Open,
                            Math.Max(lastCandle.High, lastWsPrice),
                            Math.Min(lastCandle.Low, lastWsPrice),
                            lastWsPrice,
                            lastCandle.Volume
                        );
                    }

                    if (higherResultData != null)
                    {
                        var hPrices = (double[])higherResultData.Value.prices.Clone();
                        if (hPrices != null && hPrices.Length > 0)
                            hPrices[^1] = lastWsPrice;
                        if (higherOhlc != null && higherOhlc.Length > 0)
                        {
                            higherOhlc = (OhlcCandle[])higherOhlc.Clone();
                            var lastCandle = higherOhlc[^1];
                            higherOhlc[^1] = new OhlcCandle(
                                lastCandle.Open,
                                Math.Max(lastCandle.High, lastWsPrice),
                                Math.Min(lastCandle.Low, lastWsPrice),
                                lastWsPrice,
                                lastCandle.Volume
                            );
                        }
                        higherResultData = (hPrices, higherResultData.Value.volumes);
                    }

                    if (lowerResultData != null)
                    {
                        var lPrices = (double[])lowerResultData.Value.prices.Clone();
                        if (lPrices != null && lPrices.Length > 0)
                            lPrices[^1] = lastWsPrice;
                        if (lowerOhlc != null && lowerOhlc.Length > 0)
                        {
                            lowerOhlc = (OhlcCandle[])lowerOhlc.Clone();
                            var lastCandle = lowerOhlc[^1];
                            lowerOhlc[^1] = new OhlcCandle(
                                lastCandle.Open,
                                Math.Max(lastCandle.High, lastWsPrice),
                                Math.Min(lastCandle.Low, lastWsPrice),
                                lastWsPrice,
                                lastCandle.Volume
                            );
                        }
                        lowerResultData = (lPrices, lowerResultData.Value.volumes);
                    }

                    Console.WriteLine($"[LivePrice] Updated last candle close of main and higher TFs to live WS price: {lastWsPrice}");
                }
            }

            double totalScore = 0;
            double totalConfidence = 0;
            double totalWeight = 0;

            // ─── ML Ensemble (нормализован к −1..+1) ───
            var (mlDirection, mlConfidence, mlPredicted) = MLForecastService.PredictNextCandles(mainPrices, isForex);
            double mlScoreNormalized = 0;
            double mlConfTotal = 0;
            int mlSubSignals = 0;

            if (mlDirection != "NEUTRAL")
            {
                double mlSign = mlDirection == "BUY" ? 1 : -1;
                mlScoreNormalized += mlSign * (mlConfidence / 100.0) * 0.5;
                mlConfTotal += mlConfidence;
                mlSubSignals++;
                Console.WriteLine($"[ML] SSA forecast={mlDirection} conf={mlConfidence:F0}%");
            }

            double linregSlope = LinearRegressionSlope(mainPrices, 20);
            if (Math.Abs(linregSlope) > 0.005)
            {
                double linregConf = Math.Clamp(Math.Abs(linregSlope) * 2000, 55, 90);
                double lrSign = linregSlope > 0 ? 1 : -1;
                mlScoreNormalized += lrSign * (linregConf / 100.0) * 0.4;
                mlConfTotal += linregConf;
                mlSubSignals++;
                Console.WriteLine($"[ML] LinReg slope={linregSlope:F4} dir={(linregSlope > 0 ? "BUY" : "PUT")} conf={linregConf:F0}%");
            }

            double momScore = 0;
            foreach (int window in new[] { 3, 5, 10, 20 })
            {
                if (mainPrices.Length > window)
                {
                    double roc = (mainPrices[^1] - mainPrices[^(window + 1)]) / mainPrices[^(window + 1)];
                    if (roc > 0.002) momScore++; else if (roc < -0.002) momScore--;
                }
            }
            if (Math.Abs(momScore) >= 2)
            {
                double momConf = Math.Clamp(Math.Abs(momScore) * 15, 55, 85);
                double momSign = momScore > 0 ? 1 : -1;
                mlScoreNormalized += momSign * (momConf / 100.0) * 0.4;
                mlConfTotal += momConf;
                mlSubSignals++;
                Console.WriteLine($"[ML] Momentum={momScore:F0} dir={(momScore > 0 ? "BUY" : "PUT")} conf={momConf:F0}%");
            }

            if (mlSubSignals > 0)
            {
                mlScoreNormalized /= mlSubSignals;
                mlScoreNormalized = Math.Clamp(mlScoreNormalized, -1, 1);
                double mlWeight = SignalTracker.GetSignalWeight("ML прогноз", 1.0);
                totalScore += mlScoreNormalized * mlWeight;
                totalConfidence += (mlConfTotal / mlSubSignals) * mlWeight;
                totalWeight += mlWeight;
            }

            // ─── LightGBM Python ML Service ───
            string lgbmDirection = "NEUTRAL";
            double lgbmConfidence = 0.5;
            string lgbmModelVersion = "disabled";
            double? lgbmAccuracy = null;

            if (mainOhlc != null && mainOhlc.Length >= 60)
            {
                try
                {
                    var lgbmResult = await MLPythonService.PredictAsync(
                        asset, timeframe, mainOhlc, isForex);

                    if (lgbmResult != null && lgbmResult.Direction != "NEUTRAL")
                    {
                        lgbmDirection = lgbmResult.Direction;
                        lgbmConfidence = lgbmResult.Confidence;
                        lgbmModelVersion = lgbmResult.ModelVersion;
                        lgbmAccuracy = lgbmResult.Accuracy;

                        // Weight: 1.2 — slightly higher than SSA ML (1.0) since LightGBM
                        // is a supervised model trained on actual historical outcomes
                        double lgbmSign = lgbmDirection == "BUY" ? 1.0 : -1.0;
                        double lgbmWeight = SignalTracker.GetSignalWeight("LightGBM", 1.2);
                        totalScore += lgbmSign * lgbmConfidence * lgbmWeight;
                        totalConfidence += lgbmConfidence * 100.0 * lgbmWeight;
                        totalWeight += lgbmWeight;

                        Console.WriteLine(
                            $"[LGBM] {lgbmDirection} conf={lgbmConfidence:F2} " +
                            $"acc={lgbmResult.Accuracy?.ToString("F3") ?? "?"} " +
                            $"model={lgbmModelVersion}");
                    }
                    else if (lgbmResult != null)
                    {
                        lgbmModelVersion = lgbmResult.ModelVersion;
                        Console.WriteLine($"[LGBM] NEUTRAL (conf={lgbmResult.Confidence:F2})");
                    }
                }
                catch (Exception lgbmEx)
                {
                    Console.WriteLine($"[LGBM] Skipped: {lgbmEx.Message}");
                }
            }

            // ─── News Analysis (нормализован к −1..+1) ───
            var newsResult = NewsAnalysisService.Analyze(asset);
            if (Math.Abs(newsResult.score) > 0.1)
            {
                double newsWeight = SignalTracker.GetSignalWeight("Новости", 0.8);
                double newsScoreNormalized = Math.Clamp(newsResult.score / 2.0, -1, 1);
                totalScore += newsScoreNormalized * newsWeight;
                totalConfidence += Math.Clamp(Math.Abs(newsResult.score) / 2.0 * 100, 50, 98) * newsWeight;
                totalWeight += newsWeight;
                Console.WriteLine($"[News] sentiment={newsResult.sentiment} score={newsResult.score:F1} normalized={newsScoreNormalized:F2}");
            }

            // ─── Bid/Ask Imbalance из WebSocket (нормализован к −1..+1) ───
            string imbalanceKey = symbol != null && symbol.EndsWith("USDT") ? symbol.Replace("USDT", "/USDT") : "";
            {
                double imbalance = MarketDataService.GetBookImbalance(imbalanceKey);
                if (Math.Abs(imbalance) > 0.1)
                {
                    double timeframeScale = 1.0;
                    if (tfLower == "m1" || tfLower == "m3" || tfLower == "m5" || tfLower.StartsWith("s"))
                    {
                        timeframeScale = 0.5;
                    }

                    double imbWeight = Math.Min(Math.Abs(imbalance) * 5, 2.0) * timeframeScale;
                    double dynamicImbWeight = SignalTracker.GetSignalWeight("Ордербук", imbWeight);
                    double imbNorm = Math.Clamp(dynamicImbWeight / 2.0, -1, 1);
                    double imbSign = imbalance > 0 ? 1 : -1;
                    totalScore += imbSign * imbNorm;
                    totalConfidence += Math.Clamp(55 + Math.Abs(imbalance) * 35, 55, 90) * dynamicImbWeight;
                    totalWeight += dynamicImbWeight;
                    Console.WriteLine($"[OrderBook] {imbalanceKey} imbalance={imbalance:F3} norm={imbSign * imbNorm:F2} (scaled by {timeframeScale:F1})");
                }
            }


            var (mainAdx, mainPdi, mainMdi) = mainOhlc != null ? ComputeTrueAdx(mainOhlc) : (20.0, 0.0, 0.0);
            double mainAtr = mainOhlc != null ? ComputeAtr(mainOhlc) : 0;

            // Store results for conflict detection
            var mainResult = ScoreTimeframe(mainPrices, mainVolumes, candles: mainOhlc, adxOverride: mainAdx, atrOverride: mainAtr, isForex: isForex);
            (double score, double confidence, double rsiVal, double emaVal, double volumeStrength, double atrVal) higherResult = default;
            double hAdx = 20.0, hPdi = 0.0, hMdi = 0.0, hAtr = 0.0;
            double conflictPenalty = 1.0;

            if (higherResultData != null)
            {
                (hAdx, hPdi, hMdi) = higherOhlc != null ? ComputeTrueAdx(higherOhlc) : (20.0, 0.0, 0.0);
                hAtr = higherOhlc != null ? ComputeAtr(higherOhlc) : 0;
                higherResult = ScoreTimeframe(higherResultData.Value.prices, higherResultData.Value.volumes, candles: higherOhlc, adxOverride: hAdx, atrOverride: hAtr, isForex: isForex);
                conflictPenalty = MfConflictPenalty(mainResult, higherResult);

                totalScore += higherResult.score;
                totalConfidence += higherResult.confidence * 2.0;
                totalWeight += 2.0;

                if (conflictPenalty < 1.0)
                {
                    totalScore -= Math.Abs(higherResult.score) * 0.5;
                    Console.WriteLine($"[TF] Higher TF conflict: score reduced by {Math.Abs(higherResult.score) * 0.5:F2}");
                }
            }

            (double score, double confidence, double rsiVal, double emaVal, double volumeStrength, double atrVal) lowerResult = default;
            if (lowerResultData != null)
            {
                var (lAdx, _, _) = lowerOhlc != null ? ComputeTrueAdx(lowerOhlc) : (20.0, 0.0, 0.0);
                double lAtr = lowerOhlc != null ? ComputeAtr(lowerOhlc) : 0;
                lowerResult = ScoreTimeframe(lowerResultData.Value.prices, lowerResultData.Value.volumes, candles: lowerOhlc, adxOverride: lAdx, atrOverride: lAtr, isForex: isForex);

                totalScore += lowerResult.score;
                totalConfidence += lowerResult.confidence * 0.5;
                totalWeight += 0.5;
            }

            // Main TF
            double indicatorWeight = SignalTracker.GetSignalWeight("Индикаторы", 1.0);
            totalScore += mainResult.score * indicatorWeight;
            totalConfidence += mainResult.confidence * indicatorWeight;
            totalWeight += indicatorWeight;

            // ─── Extra TF scoring for major pairs ───
            int tfAgreement = 1;
            if (isMajor)
            {
                double mainDirSign = mainResult.score >= 0 ? 1 : -1;
                foreach (var r in extraResults)
                {
                    var s = ScoreTimeframe(r.prices, r.volumes);
                    if ((s.score >= 0 && mainDirSign > 0) || (s.score < 0 && mainDirSign < 0))
                        tfAgreement++;
                }

                if (higherResultData != null)
                {
                    if ((higherResult.score >= 0 && mainDirSign > 0) || (higherResult.score < 0 && mainDirSign < 0))
                        tfAgreement++;
                }
                if (lowerResultData != null)
                {
                    if ((lowerResult.score >= 0 && mainDirSign > 0) || (lowerResult.score < 0 && mainDirSign < 0))
                        tfAgreement++;
                }

                int totalTfsEvaluated = 1 + extraResults.Count + (higherResultData != null ? 1 : 0) + (lowerResultData != null ? 1 : 0);
                Console.WriteLine($"[Major] {asset} TF agreement: {tfAgreement}/{totalTfsEvaluated}");
            }

            // Compute higher timeframe info for Claude context
            string? higherTfInfo = null;
            if (higherResultData != null)
            {
                var hPrices = higherResultData.Value.prices;
                var (hMacd, hMacdSig) = ComputeMacd(hPrices, hPrices.Length - 1);
                double hBbZ = ComputeBollingerZscore(hPrices, 20);
                
                higherTfInfo = $"Timeframe: {higherTf}, Score: {higherResult.score:F2}, RSI: {higherResult.rsiVal:F1}, EMA: {higherResult.emaVal:F5}, MACD: {hMacd:F6}, Signal: {hMacdSig:F6}, ADX: {hAdx:F1}, +DI: {hPdi:F1}, -DI: {hMdi:F1}, ATR: {hAtr:F6}, BBz: {hBbZ:F2}";
            }

            // ─── Claude AI Signal with OHLC pattern analysis ───
            var (macdLine, macdSig) = ComputeMacd(mainPrices, mainPrices.Length - 1);
            double bbZscore = ComputeBollingerZscore(mainPrices, 20);
            double claudeImbalance = !string.IsNullOrEmpty(imbalanceKey)
                ? MarketDataService.GetBookImbalance(imbalanceKey) : 0;

            // Look up OHLC candles from cache (filled during FetchBinanceCandles/TwelveData)
            string ohlcKey = mainOhlcKey;
            var ohlcCandles = GetOhlcCandles(ohlcKey);
            // Take last 30 candles for Claude (enough for pattern detection, keeps token count low)
            var ohlcForClaude = ohlcCandles != null && ohlcCandles.Length > 30
                ? ohlcCandles[^30..] : ohlcCandles;

            // Detect candlestick patterns and support/resistance levels
            var detectedPatterns = ohlcCandles != null ? PatternDetector.DetectPatterns(ohlcCandles) : new List<string>();
            var (supports, resistances) = PatternDetector.CalculateLevels(mainPrices, isForex);
            Console.WriteLine($"[Patterns] {string.Join(", ", detectedPatterns)}");
            string priceFormat = isForex ? (mainPrices[^1] > 100 ? "F3" : "F5") : (mainPrices[^1] > 100 ? "F1" : "F4");
            string FmtLevels(double[] levels) => levels.Length == 0 ? "-" : string.Join(" │ ", levels.Select(l => l.ToString(priceFormat, System.Globalization.CultureInfo.InvariantCulture)));
            Console.WriteLine($"[Levels] S: {FmtLevels(supports)} R: {FmtLevels(resistances)}");

            // ─── Candle status + caching ───
            int timeframeSec = TimeframeSeconds(timeframe);
            int binanceSec = BinanceIntervalToSeconds(mainInterval);
            long nowSec = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            long candleId = nowSec / timeframeSec;
            int candleSecondsRemaining = (int)(timeframeSec - nowSec % timeframeSec);
            string cacheKey = $"claude_{asset}_{timeframe}_{candleId}";

            (string direction, double probability, string reasoning, string modelName) claudeResult;
            bool isSubMinute = timeframe.ToLower().StartsWith("s");

            if (_cache.TryGetValue(cacheKey, out object? cached) && cached is ValueTuple<string, double, string, string> cachedTuple)
            {
                claudeResult = cachedTuple;
                Console.WriteLine($"[Cache] HIT for {cacheKey}");
            }
            else
            {
                claudeResult = await ClaudeSignalService.AnalyzeSignal(
                    asset, mainPrices, mainVolumes,
                    mainResult.rsiVal, mainResult.emaVal, macdLine, macdSig,
                    mainAdx, bbZscore, mainResult.volStrengthVal, claudeImbalance,
                    higherTfInfo, ohlcForClaude, detectedPatterns, supports, resistances,
                    timeframe, candleSecondsRemaining, timeframeSec, mainAtr, mainPdi, mainMdi);
                int cacheTtlSec = Math.Max(candleSecondsRemaining + 2, 3);
                _cache.Set(cacheKey, claudeResult, TimeSpan.FromSeconds(cacheTtlSec));
                Console.WriteLine($"[Cache] Stored {cacheKey}, TTL={cacheTtlSec}s");
            }
            if (claudeResult.direction != "NEUTRAL")
            {
                double claudeSign = claudeResult.direction == "BUY" ? 1 : -1;
                double claudeWeight = SignalTracker.GetSignalWeight("Claude AI", 1.5);
                totalScore += claudeSign * (claudeResult.probability / 100.0) * claudeWeight;
                totalConfidence += claudeResult.probability * claudeWeight;
                totalWeight += claudeWeight;
                Console.WriteLine($"[Claude] dir={claudeResult.direction} prob={claudeResult.probability:F0}% weight={claudeWeight:F2} reasoning={claudeResult.reasoning}");
            }
            else
            {
                // If AI is offline or neutral, boost the local math fallback with Price Action and Imbalance!
                if (detectedPatterns != null && detectedPatterns.Count > 0)
                {
                    double patternScore = 0;
                    foreach (var pattern in detectedPatterns)
                    {
                        if (pattern.EndsWith("_bullish") || pattern == "HAMMER")
                            patternScore += 0.4;
                        else if (pattern.EndsWith("_bearish") || pattern == "HANGING_MAN")
                            patternScore -= 0.4;
                    }
                    totalScore += patternScore;
                    Console.WriteLine($"[Math-PA] Added candlestick pattern score: {patternScore:F2} (Patterns: {string.Join(", ", detectedPatterns)})");
                }

                // Add real-time order book imbalance
                double bookImbalance = !string.IsNullOrEmpty(imbalanceKey) ? MarketDataService.GetBookImbalance(imbalanceKey) : 0;
                if (Math.Abs(bookImbalance) > 0.15)
                {
                    double imbalanceScore = bookImbalance > 0 ? 0.3 : -0.3;
                    totalScore += imbalanceScore;
                    Console.WriteLine($"[Math-Imbalance] Added book imbalance score: {imbalanceScore:F2} (Imbalance: {bookImbalance:F2})");
                }
            }

            // ─── Micro-momentum for sub-minute timeframes ───
            // For 5-second trades, the last 1-2 candle direction is more relevant than EMA/MACD trends
            if (isSubMinute && mainPrices.Length >= 3)
            {
                double lastChange = (mainPrices[^1] - mainPrices[^2]) / (mainPrices[^2] + 1e-10) * 10000; // bps
                double prevChange = (mainPrices[^2] - mainPrices[^3]) / (mainPrices[^3] + 1e-10) * 10000; // bps

                // If last two candles agree on direction → strong micro-signal
                if (lastChange > 1.0 && prevChange > 1.0)
                {
                    totalScore += 1.0;
                    Console.WriteLine($"[Micro] Last 2 candles UP → +1.0 (Δ1={lastChange:F1}bps, Δ2={prevChange:F1}bps)");
                }
                else if (lastChange < -1.0 && prevChange < -1.0)
                {
                    totalScore -= 1.0;
                    Console.WriteLine($"[Micro] Last 2 candles DOWN → -1.0 (Δ1={lastChange:F1}bps, Δ2={prevChange:F1}bps)");
                }
                else if (Math.Abs(lastChange) > 3.0)
                {
                    double microBoost = lastChange > 0 ? 0.6 : -0.6;
                    totalScore += microBoost;
                    Console.WriteLine($"[Micro] Last candle strong → {microBoost:+0.0;-0.0} (Δ={lastChange:F1}bps)");
                }
                totalWeight += 0.5;
            }

            // ─── Short-term momentum (3-bar + 5-bar) ───
            double mom3 = mainPrices.Length >= 4
                ? (mainPrices[^1] - mainPrices[^4]) / mainPrices[^4] * 100 : 0;
            double mom5 = mainPrices.Length >= 6
                ? (mainPrices[^1] - mainPrices[^6]) / mainPrices[^6] * 100 : 0;
            int momentumSignal = 0;
            // Lower thresholds for forex (where 0.15% is a huge move on 1-min candles)
            double momThresh3 = isForex ? 0.03 : 0.15;
            double momThresh5 = isForex ? 0.05 : 0.2;
            if (mom3 > momThresh3 || mom5 > momThresh5) momentumSignal = 1;
            else if (mom3 < -momThresh3 || mom5 < -momThresh5) momentumSignal = -1;

            int rawProb = totalWeight > 0
                ? (int)Math.Clamp(totalConfidence / totalWeight, 50, 98)
                : 50;

            if (conflictPenalty < 1.0)
                rawProb = Math.Max(50, rawProb - 8);

            // ─── Калибровка вероятности (только после 10+ предсказаний) ───
            var overallCalibStats = SignalTracker.GetOverallStats();
            double accuracy = overallCalibStats.WinRate / 100.0;
            int totalPreds = overallCalibStats.Verified;
            int probability;
            if (accuracy > 0 && totalPreds >= 10)
            {
                probability = (int)Math.Round(rawProb * 0.7 + (accuracy * 100) * 0.3);
                probability = Math.Clamp(probability, 55, 98);
            }
            else
            {
                probability = rawProb;
            }

            // ─── Sub-minute override: simple decisive scoring ───
            // Proportional ensemble produces near-zero for forex (score=-0.03).
            // For 5-second trades, use last-candle direction + EMA + RSI instead.
            if (isSubMinute && mainPrices.Length >= 5)
            {
                double overrideScore = 0;

                // 1. Last candle direction & magnitude (in basis points)
                double changeBps = (mainPrices[^1] - mainPrices[^2]) / (mainPrices[^2] + 1e-10) * 10000;
                double prevChangeBps = (mainPrices[^2] - mainPrices[^3]) / (mainPrices[^3] + 1e-10) * 10000;

                // Filter out micro-noise (if change is less than 0.02 bps, treat as flat)
                if (Math.Abs(changeBps) > 0.02)
                {
                    overrideScore += changeBps > 0 ? 1.0 : -1.0;
                }


                // 2. Last 3 candles net direction
                double d3 = mainPrices[^1] - mainPrices[^4];
                if (Math.Abs(d3) > 1e-7)
                {
                    overrideScore += d3 > 0 ? 0.8 : -0.8;
                }

                // 3. Agreeing micro-trend (consecutive ticks)
                if (changeBps > 0.2 && prevChangeBps > 0.2)
                    overrideScore += 0.5;
                else if (changeBps < -0.2 && prevChangeBps < -0.2)
                    overrideScore -= 0.5;

                // 4. Price vs EMA9 (trend bias)
                double emaPos = mainPrices[^1] - mainResult.emaVal;
                if (Math.Abs(emaPos) > 1e-7)
                {
                    overrideScore += emaPos > 0 ? 0.4 : -0.4;
                }

                // 5. RSI mean-reversion at extremes
                double rsiVal = mainResult.rsiVal;
                if (rsiVal > 75) overrideScore -= 1.2;       // strong overbought → expect down
                else if (rsiVal < 25) overrideScore += 1.2;  // strong oversold → expect up
                else if (rsiVal > 58) overrideScore += 0.2;  // mild bullish
                else if (rsiVal < 42) overrideScore -= 0.2;  // mild bearish

                totalScore = overrideScore;
                Console.WriteLine($"[SubMin] Override score={totalScore:F2} (change={changeBps:F2}bps, d3={d3:F6}, emaPos={emaPos:F6}, rsi={rsiVal:F1})");
            }

            // ─── SNIPER MODE: Направление и вероятность ───
            // Правило #1: Если Claude не уверен → НЕ ТОРГУЕМ
            // Правило #2: Если Claude уверен, но индикаторы противоречат → НЕ ТОРГУЕМ
            // Правило #3: Показываем РЕАЛЬНУЮ вероятность, без раздувания

            string direction;

            if (claudeResult.direction != "NEUTRAL" && claudeResult.probability >= 65)
            {
                // Claude дал сигнал с хорошей уверенностью
                // Проверяем не противоречат ли индикаторы
                int claudeSign = claudeResult.direction == "BUY" ? 1 : -1;
                int indicatorSign = mainResult.score >= 0 ? 1 : -1;
                
                if (claudeSign == indicatorSign || Math.Abs(mainResult.score) < 0.2)
                {
                    // Индикаторы подтверждают или нейтральны → берём сигнал Claude
                    direction = claudeResult.direction;
                    probability = (int)Math.Round(claudeResult.probability);
                    
                    // Бонус за совпадение с momentum
                    if (momentumSignal == claudeSign)
                        probability = Math.Min(probability + 5, 95);
                    
                    // Штраф за конфликт TF
                    if (conflictPenalty < 1.0)
                        probability = Math.Max(55, probability - 10);
                        
                    Console.WriteLine($"[Sniper] Claude signal ACCEPTED: {direction} {probability}%");
                }
                else
                {
                    // Индикаторы сильно противоречат Claude → пропускаем
                    direction = "NEUTRAL";
                    probability = 50;
                    Console.WriteLine($"[Sniper] Claude said {claudeResult.direction} but indicators disagree (score={mainResult.score}) → NEUTRAL");
                }
            }
            else
            {
                // Claude не уверен (NEUTRAL или probability < 65)
                // Используем математику с адаптивным порогом
                bool aiWasAvailable = !string.IsNullOrEmpty(claudeResult.modelName) && claudeResult.modelName != "Математический анализ";
                double absScore = Math.Abs(totalScore);
                int scoreSign = totalScore > 0.02 ? 1 : totalScore < -0.02 ? -1 : 0;

                // Пороги откалиброваны для пропорциональных оценок (диапазон ~±2.0)
                double baseMinScore = aiWasAvailable ? 0.50 : 0.30;

                // Для Forex-пар (где нет стакана Binance) снижаем порог на 20%
                if (isForex)
                {
                    baseMinScore *= 0.8;
                }
                double volatilityRatio = CalculateVolatilityRatio(mainPrices);
                double minScore = baseMinScore;

                if (volatilityRatio < 0.7)
                {
                    minScore = Math.Max(0.15, baseMinScore - 0.05);
                    Console.WriteLine($"[Volatility-Sniper] Low volatility (ratio={volatilityRatio:F2}). Adjusted minScore from {baseMinScore:F2} to {minScore:F2}");
                }
                else if (volatilityRatio > 1.35)
                {
                    minScore = Math.Min(0.60, baseMinScore + 0.10);
                    Console.WriteLine($"[Volatility-Sniper] High volatility (ratio={volatilityRatio:F2}). Adjusted minScore from {baseMinScore:F2} to {minScore:F2}");
                }
                else
                {
                    Console.WriteLine($"[Volatility-Sniper] Normal volatility (ratio={volatilityRatio:F2}). minScore is {minScore:F2}");
                }

                // momentum не должен противоречить totalScore для сильного тренда
                bool momentumOk = momentumSignal == 0 || momentumSignal == scoreSign;

                string candidateDir = scoreSign > 0 ? "BUY" : scoreSign < 0 ? "PUT" : "NEUTRAL";
                double currentPrice = mainPrices[^1];
                double nearestSupport = 0;
                double nearestResistance = 0;

                if (supports != null && supports.Length > 0)
                {
                    foreach (var s in supports)
                    {
                        if (s < currentPrice && s > 0) { nearestSupport = s; break; }
                    }
                }
                if (resistances != null && resistances.Length > 0)
                {
                    foreach (var r in resistances)
                    {
                        if (r > currentPrice && r > 0) { nearestResistance = r; break; }
                    }
                }

                // Adaptive safe distance using ATR
                double safeBuffer = mainAtr > 0 ? 1.2 * mainAtr : 0.00015 * currentPrice;
                bool blockedByLevel = false;
                string blockReason = "";

                if (candidateDir == "BUY" && nearestResistance > 0 && (nearestResistance - currentPrice) < safeBuffer)
                {
                    blockedByLevel = true;
                    blockReason = $"сопротивление {nearestResistance.ToString(priceFormat, System.Globalization.CultureInfo.InvariantCulture)}";
                }
                else if (candidateDir == "PUT" && nearestSupport > 0 && (currentPrice - nearestSupport) < safeBuffer)
                {
                    blockedByLevel = true;
                    blockReason = $"поддержка {nearestSupport.ToString(priceFormat, System.Globalization.CultureInfo.InvariantCulture)}";
                }

                // --- 1. КРАСНЫЙ СВЕТ: Полный флэт (очень низкий балл консенсуса) ---
                double flatThreshold = isSubMinute ? 0.08 : 0.04;
                if (absScore < flatThreshold)
                {
                    direction = "NEUTRAL";
                    probability = 50;

                    claudeResult.modelName = "Математический анализ";
                    claudeResult.direction = "NEUTRAL";
                    claudeResult.probability = 50;
                    claudeResult.reasoning = isSubMinute 
                        ? "Микро-тренд на секундном графике слаб или неустойчив. Высокий риск шума, рекомендуется воздержаться от сделок."
                        : "Рынок находится в мертвом флэте или индикаторы полностью противоречат друг другу. Рекомендуется воздержаться от сделок.";

                    Console.WriteLine($"[Sniper-Flat] Neutral due to flat/noise (score={totalScore:F2} < {flatThreshold})");
                }
                // --- 2. ЖЕЛТЫЙ СВЕТ: Близко уровень поддержки/сопротивления или слабый тренд ---
                else if (blockedByLevel || absScore < minScore || !momentumOk)
                {
                    direction = candidateDir;
                    probability = isSubMinute 
                        ? Math.Clamp(58 + (int)Math.Round(absScore * 10), 55, 65)
                        : Math.Clamp(62 + (int)Math.Round(absScore * 12), 58, 72);

                    if (string.IsNullOrEmpty(claudeResult.modelName) || claudeResult.modelName == "Математический анализ")
                    {
                        claudeResult.modelName = "Математический анализ";
                        claudeResult.direction = direction;
                        claudeResult.probability = probability;

                        if (blockedByLevel)
                        {
                            claudeResult.reasoning = $"Внимание: близко {blockReason}. Сигнал сформирован локально, рекомендуется повышенная осторожность.";
                            Console.WriteLine($"[Sniper-Warning] Level block ({blockReason}) -> downgraded signal: {direction} {probability}%");
                        }
                        else if (!momentumOk)
                        {
                            claudeResult.reasoning = "Внимание: импульс противоречит общему тренду. Локальный откат, рекомендуется осторожность.";
                            Console.WriteLine($"[Sniper-Warning] Momentum conflict -> downgraded signal: {direction} {probability}%");
                        }
                        else
                        {
                            claudeResult.reasoning = "Внимание: слабый тренд. Возможны ложные колебания, рекомендуется осторожность.";
                            Console.WriteLine($"[Sniper-Warning] Weak trend (score={totalScore:F2} < {minScore:F2}) -> downgraded signal: {direction} {probability}%");
                        }
                    }
                }
                // --- 3. ЗЕЛЕНЫЙ СВЕТ: Сильный тренд с подтверждением ---
                else
                {
                    direction = candidateDir;

                    double rawProbFloat;
                    if (isSubMinute)
                    {
                        rawProbFloat = 65.0 + (absScore - minScore) * 8.0;
                        probability = Math.Clamp((int)Math.Round(rawProbFloat), 62, 80);
                    }
                    else
                    {
                        rawProbFloat = 75.0 + (absScore - minScore) * 15.0;
                        probability = Math.Clamp((int)Math.Round(rawProbFloat), 75, 95);
                    }

                    if (string.IsNullOrEmpty(claudeResult.modelName) || claudeResult.modelName == "Математический анализ")
                    {
                        claudeResult.modelName = "Математический анализ";
                        claudeResult.direction = direction;
                        claudeResult.probability = probability;
                        claudeResult.reasoning = "Сигнал сформирован на основе сильного технического консенсуса индикаторов (RSI, EMA, MACD) и локального ML.";
                    }

                    Console.WriteLine($"[Sniper-Strong] Strong signal: {direction} {probability}% (score={totalScore:F2})");
                }
            }

            // ─── TF consensus boost for major pairs ───
            if (direction != "NEUTRAL" && isMajor && tfAgreement >= 5)
            {
                probability = Math.Clamp(probability + 5, 55, 95);
                Console.WriteLine($"[Major] TF agreement {tfAgreement}/7 → probability boosted to {probability}%");
            }

            // ─── Signal Tracker (only track real signals, not NEUTRAL) ───
            int expiryCandles = GetExpiryCandles(timeframe);

            // Always update the price cache for verification (even for NEUTRAL)
            SignalTracker.UpdatePrice(symbol ?? asset, mainPrices[^1]);

            if (direction != "NEUTRAL")
            {
                var sourceDirs = new Dictionary<string, string>
                {
                    { "Индикаторы", mainResult.score >= 0 ? "BUY" : "PUT" },
                    { "ML прогноз", mlDirection },
                    { "LightGBM", lgbmDirection },
                    { "Новости", newsResult.score > 0 ? "BUY" : newsResult.score < 0 ? "PUT" : "NEUTRAL" },
                    { "Claude AI", claudeResult.direction }
                };

                double imb = MarketDataService.GetBookImbalance(imbalanceKey);
                sourceDirs["Ордербук"] = Math.Abs(imb) > 0.1 ? (imb > 0 ? "BUY" : "PUT") : "NEUTRAL";

                SignalTracker.RecordPrediction(
                    direction, asset, timeframe, mainPrices[^1],
                    expiryCandles, timeframeSec, isForex, symbol, sourceDirs);
            }

            // Expiry: вычисляем сколько свечей нужно для закрытия сделки
            var overallStats = SignalTracker.GetOverallStats();
            var assetStats   = SignalTracker.GetStats(asset, timeframe);

            int totalExpirySec = timeframeSec * expiryCandles;
            string durationText = isSubMinute ? $"{totalExpirySec} сек (экспирация)" : $"{timeframe.ToUpper()} ({expiryCandles} свечи)";

            return new
            {
                direction,
                probability,
                duration = durationText,
                expiryCandles,
                chartData = mainPrices,
                rsi = Math.Round(mainResult.rsiVal, 1),
                ema = Math.Round(mainResult.emaVal, 2),
                volumeStrength = Math.Round(mainResult.volStrengthVal, 2),
                tfConflict = conflictPenalty < 1.0,
                mlDirection,
                mlConfidence = Math.Round(mlConfidence, 0),
                lgbmDirection,
                lgbmConfidence = Math.Round(lgbmConfidence * 100, 0),
                lgbmAccuracy = lgbmAccuracy.HasValue ? Math.Round(lgbmAccuracy.Value * 100, 1) : (double?)null,
                lgbmModelVersion,
                newsSentiment = newsResult.sentiment,
                newsScore = Math.Round(newsResult.score, 1),
                newsSummary = newsResult.summary,
                newsHeadlines = newsResult.headlines,
                claudeDirection = claudeResult.direction,
                claudeProbability = Math.Round(claudeResult.probability, 0),
                claudeReasoning = claudeResult.reasoning,
                aiModel = claudeResult.modelName,
                // ── Accuracy stats ──
                winRateOverall    = overallStats.HasData ? overallStats.WinRate : (double?)null,
                winRateAsset      = assetStats.HasData   ? assetStats.WinRate   : (double?)null,
                signalsVerified   = overallStats.Verified,
                signalsPending    = SignalTracker.GetPendingCount()
            };
        }
        catch (Exception ex)
        {
            LastExceptionMessage = ex.ToString();
            Console.WriteLine($"[ERR] Analysis failed: {ex.Message}");
            return GetMomentumPrediction(asset, timeframe);
        }
    }

    public static string SanitizeAsset(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        return raw.ToUpper()
            .Replace("OTC", "")
            .Replace("ОТС", "") // Cyrillic
            .Replace(" ", "")
            .Replace("/", "")
            .Replace("-", "")
            .Replace("_", "")
            .Trim();
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
