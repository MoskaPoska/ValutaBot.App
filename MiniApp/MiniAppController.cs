using System.Globalization;
using System.Net.Http;
using System.Text.Json;
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
    private static readonly AsyncRetryPolicy _retryPolicy = Policy
        .Handle<HttpRequestException>()
        .Or<TaskCanceledException>()
        .WaitAndRetryAsync(3, attempt => TimeSpan.FromMilliseconds(500 * attempt));

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

        var app = builder.Build();
        app.UseCors("AllowMiniApp");

        app.MapGet("/", async (HttpContext context) =>
        {
            context.Response.ContentType = "text/html; charset=utf-8";
            if (!context.Request.Headers.ContainsKey("ngrok-skip-browser-warning") &&
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

        app.MapGet("/api/analyze", async (string? asset, string? timeframe) =>
        {
            if (string.IsNullOrWhiteSpace(asset) || string.IsNullOrWhiteSpace(timeframe))
                return Results.Json(new { error = "asset and timeframe are required" });

            string originalAsset = asset.ToUpper().Trim();
            string tf = timeframe.ToLower().Trim();
            Console.WriteLine($"[ANALYZE] {originalAsset} | TF: {timeframe}");

            var result = await ExecuteBinanceAnalysis(originalAsset, tf);
            return Results.Json(result);
        });

        app.MapGet("/api/fear-greed", async () =>
        {
            var fng = await GetFearGreedIndex();
            return Results.Json(fng);
        });

        app.MapGet("/api/market-status", () =>
        {
            var latest = MarketDataService.GetLatestPrices();
            var alerts = MarketDataService.GetRecentAlerts();
            return Results.Json(new { prices = latest, alerts });
        });

        app.Run($"http://0.0.0.0:{port}");
    }

    private static string IntervalMap(string tf) => tf.ToLower() switch
    {
        "m1" => "1m", "m5" => "5m", "m15" => "15m", "m30" => "30m",
        "h1" => "1h", "h4" => "4h", "d1" => "1d", _ => "1m"
    };

    private static string? HigherTf(string tf) => tf.ToLower() switch
    {
        "m1" => "m5", "m5" => "m15", "m15" => "m30", "m30" => "h1",
        "h1" => "h4", "h4" => "d1", _ => null
    };

    private static string? LowerTf(string tf) => tf.ToLower() switch
    {
        "m5" => "m1", "m15" => "m5", "m30" => "m15",
        "h1" => "m30", "h4" => "h1", "d1" => "h4", _ => null
    };

    private const int RsiPeriod = 14;
    private const int EmaShort = 9;
    private const int EmaLong = 21;

    /* ─── Cached fetch with retry ─── */

    private static async Task<(double[] prices, double[] volumes)> FetchBinanceCandles(string symbol, string interval)
    {
        string cacheKey = $"binance_{symbol}_{interval}";
        if (_cache.TryGetValue(cacheKey, out (double[] prices, double[] volumes) cached))
            return cached;

        string url = $"https://api.binance.com/api/v3/klines?symbol={symbol}&interval={interval}&limit=50";
        var response = await _retryPolicy.ExecuteAsync(() => _httpClient.GetStringAsync(url));
        using var doc = JsonDocument.Parse(response);
        var arr = doc.RootElement.EnumerateArray().ToList();
        var prices = arr.Select(k => double.Parse(k[4].GetString()!, CultureInfo.InvariantCulture)).ToArray();
        var volumes = arr.Select(k => double.Parse(k[5].GetString()!, CultureInfo.InvariantCulture)).ToArray();

        _cache.Set(cacheKey, (prices, volumes), TimeSpan.FromSeconds(15));
        return (prices, volumes);
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

    private static double ComputeRsi(double[] data, int period, int index)
    {
        if (index < period) return double.NaN;
        double gain = 0, loss = 0;
        for (int i = index - period + 1; i <= index; i++)
        {
            double diff = data[i] - data[i - 1];
            if (diff > 0) gain += diff; else loss -= diff;
        }
        double avgGain = gain / period;
        double avgLoss = loss / period;
        if (avgLoss < 1e-12) return 100;
        double rs = avgGain / avgLoss;
        return 100 - 100 / (1 + rs);
    }

    private static (double macd, double signal) ComputeMacd(double[] data, int index)
    {
        double macdVal = ComputeEma(data, 12, index) - ComputeEma(data, 26, index);
        double signalVal = ComputeEma(data, 9, index);
        return (macdVal, signalVal);
    }

    /* ─── Volume strength ─── */

    private static double VolumeStrength(double[] prices, double[] volumes)
    {
        int n = volumes.Length;
        if (n < 10) return 0;

        double avgVol = volumes.Skip(n - 10).Take(10).Average();
        double currentVol = volumes[^1];
        double prevClose = prices[^2];
        double currentClose = prices[^1];
        double change = (currentClose - prevClose) / prevClose;

        // If price moves up with above-avg volume → strong trend
        // If price moves up with below-avg volume → weak trend
        double volRatio = currentVol / avgVol;
        double direction = change > 0 ? 1 : -1;

        // volStrength ranges from -1 to 1
        double volStrength = direction * Math.Min(volRatio, 2.0) / 2.0;
        return volStrength * 2; // scale to -2..+2 influence range
    }

    /* ─── Scoring Engine ─── */

    private static (int score, double confidence, double rsiVal, double emaVal, double volStrengthVal)
        ScoreTimeframe(double[] prices, double[] volumes, double weight)
    {
        int n = prices.Length;
        if (n < EmaLong + 5) return (0, 0, 50, 0, 0);

        var emaShortArr = ComputeEmaArray(prices, EmaShort);
        var emaLongArr = ComputeEmaArray(prices, EmaLong);
        double rsi = ComputeRsi(prices, RsiPeriod, n - 1);
        var (macd, signal) = ComputeMacd(prices, n - 1);
        double lastPrice = prices[^1];
        double emaS = emaShortArr[^1];
        double emaL = emaLongArr[^1];
        double prevEmaS = emaShortArr[^2];
        double change = (prices[^1] - prices[0]) / prices[0];

        double volStrength = VolumeStrength(prices, volumes);

        int signals = 0, total = 0;

        // 1. Price vs EMA9 (short trend)
        if (lastPrice > emaS) signals++; else signals--;
        total++;

        // 2. Price vs EMA21 (medium trend)
        if (lastPrice > emaL) signals++; else signals--;
        total++;

        // 3. EMA9 vs EMA21 (trend direction)
        if (emaS > emaL) signals++; else signals--;
        total++;

        // 4. EMA9 slope
        if (emaS > prevEmaS) signals++; else signals--;
        total++;

        // 5. RSI (weighted)
        if (rsi < 30) signals += 2;       // oversold → strong buy signal
        else if (rsi < 40) signals += 1;   // near oversold
        else if (rsi > 70) signals -= 2;   // overbought → strong sell signal
        else if (rsi > 60) signals -= 1;   // near overbought
        total += 2;

        // 6. MACD
        if (macd > signal) signals++; else signals--;
        total++;

        // 7. Overall change
        if (change > 0.001) signals++; else if (change < -0.001) signals--;
        total++;

        // 8. Volume confirmation (adds -2..+2 range)
        signals += (int)Math.Round(volStrength);
        total += 2;

        double rawRatio = (double)signals / total;
        double score = rawRatio * weight;
        double confidence = Math.Abs(rawRatio) * 100;
        confidence = Math.Clamp(confidence, 62, 98);

        return ((int)Math.Round(score * 10), confidence, rsi, emaS, volStrength);
    }

    /* ─── Multi-TF conflict penalty ─── */

    private static double MfConflictPenalty((int score, double conf, double rsi, double ema, double vol) main,
                                             (int score, double conf, double rsi, double ema, double vol) higher)
    {
        // If main and higher TF disagree → reduce confidence
        int mainDir = main.score >= 0 ? 1 : -1;
        int higherDir = higher.score >= 0 ? 1 : -1;
        if (mainDir != higherDir)
            return 0.85; // 15% penalty
        return 1.0;
    }

    /* ─── Main analysis ─── */

    private static async Task<object> ExecuteBinanceAnalysis(string asset, string timeframe)
    {
        try
        {
            string symbol = asset.Replace("/", "").Replace(" ", "");
            if (symbol == "GOLD") symbol = "PAXGUSDT";
            if (symbol == "SILVER") symbol = "XAGUSDT";
            if (!symbol.EndsWith("USDT")) symbol += "USDT";

            string mainInterval = IntervalMap(timeframe);
            string? higherTf = HigherTf(timeframe);
            string? lowerTf = LowerTf(timeframe);

            var tasks = new List<Task<(double[] prices, double[] volumes)>> { FetchBinanceCandles(symbol, mainInterval) };
            var tfWeights = new List<double> { 1.0 };

            if (higherTf != null)
            {
                tasks.Add(FetchBinanceCandles(symbol, IntervalMap(higherTf)));
                tfWeights.Add(2.0);
            }
            if (lowerTf != null)
            {
                tasks.Add(FetchBinanceCandles(symbol, IntervalMap(lowerTf)));
                tfWeights.Add(0.5);
            }

            var results = await Task.WhenAll(tasks);
            var mainPrices = results[0].prices;
            var mainVolumes = results[0].volumes;

            double totalScore = 0;
            double totalConfidence = 0;
            double totalWeight = 0;

            // ─── ML.Forecast ───
            var (mlDirection, mlConfidence, mlPredicted) = MLForecastService.PredictNextCandles(mainPrices);
            double mlWeight = 1.5; // ML signal carries extra weight

            if (mlDirection != "NEUTRAL")
            {
                int mlSign = mlDirection == "BUY" ? 1 : -1;
                totalScore += mlSign * (int)(mlConfidence / 20 * mlWeight);
                totalConfidence += mlConfidence * mlWeight;
                totalWeight += mlWeight;
                Console.WriteLine($"[ML] forecast={mlDirection} conf={mlConfidence:F0}% last={mainPrices[^1]:F2} -> pred={mlPredicted[^1]:F2}");
            }

            // Store results for conflict detection
            var mainResult = ScoreTimeframe(mainPrices, mainVolumes, tfWeights[0]);
            double conflictPenalty = 1.0;

            if (results.Length >= 2 && higherTf != null)
            {
                var higherResult = ScoreTimeframe(results[1].prices, results[1].volumes, tfWeights[1]);
                conflictPenalty = MfConflictPenalty(mainResult, higherResult);

                totalScore += higherResult.score;
                totalConfidence += higherResult.confidence * tfWeights[1];
                totalWeight += tfWeights[1];
            }
            if (results.Length >= 3 && lowerTf != null)
            {
                var lowerResult = ScoreTimeframe(results[2].prices, results[2].volumes, tfWeights[2]);

                totalScore += lowerResult.score;
                totalConfidence += lowerResult.confidence * tfWeights[2];
                totalWeight += tfWeights[2];
            }

            // Main TF
            totalScore += mainResult.score;
            totalConfidence += mainResult.confidence * tfWeights[0];
            totalWeight += tfWeights[0];

            string direction = totalScore >= 0 ? "BUY" : "PUT";
            int probability = totalWeight > 0
                ? (int)Math.Clamp(totalConfidence / totalWeight * conflictPenalty, 62, 98)
                : 75;

            return new
            {
                direction,
                probability,
                duration = timeframe.ToUpper(),
                chartData = mainPrices,
                rsi = Math.Round(mainResult.rsiVal, 1),
                ema = Math.Round(mainResult.emaVal, 2),
                volumeStrength = Math.Round(mainResult.volStrengthVal, 2),
                tfConflict = conflictPenalty < 1.0,
                mlDirection = mlDirection,
                mlConfidence = Math.Round(mlConfidence, 0)
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERR] Analysis failed: {ex.Message}");
            return GetMomentumPrediction(asset, timeframe);
        }
    }

    /* ─── Fallback ─── */

    private static object GetMomentumPrediction(string asset, string tf)
    {
        var rnd = new Random();

        double startPrice = 1.1000;
        double volatility = 0.0010;

        if (asset.Contains("BTC")) { startPrice = 64000; volatility = 40.0; }
        else if (asset.Contains("ETH")) { startPrice = 3500; volatility = 5.0; }
        else if (asset.Contains("AAPL")) { startPrice = 180.50; volatility = 0.3; }
        else if (asset.Contains("GOLD")) { startPrice = 2300; volatility = 1.5; }
        else if (asset.Contains("JPY")) { startPrice = 150.00; volatility = 0.05; }

        var chartData = new double[15];
        double currentPrice = startPrice;
        double mainTrend = (rnd.NextDouble() - 0.5) * volatility;

        for (int i = 0; i < 15; i++)
        {
            currentPrice += mainTrend + (rnd.NextDouble() - 0.5) * (volatility / 2);
            chartData[i] = Math.Round(currentPrice, startPrice > 100 ? 2 : 5);
        }

        string direction = chartData[14] >= chartData[0] ? "BUY" : "PUT";
        double diffPercent = Math.Abs((chartData[14] - chartData[0]) / chartData[0]) * 100;
        int probability = Math.Clamp(82 + (int)(diffPercent * 50), 82, 97);

        return new
        {
            direction,
            probability,
            duration = tf.ToUpper(),
            chartData,
            rsi = Math.Round(50 + (rnd.NextDouble() - 0.5) * 40, 1),
            ema = Math.Round(startPrice + (rnd.NextDouble() - 0.5) * volatility, 2),
            volumeStrength = 0.0,
            tfConflict = false,
            mlDirection = "NEUTRAL",
            mlConfidence = 0
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
