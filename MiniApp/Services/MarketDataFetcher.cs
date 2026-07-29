using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Polly;
using Polly.Retry;

namespace ValutaBot.MiniApp;

public class ExchangeUnavailableException : Exception
{
    public string UserFriendlyMessage { get; }

    public ExchangeUnavailableException(string message, string userFriendlyMessage, Exception? inner = null)
        : base(message, inner)
    {
        UserFriendlyMessage = userFriendlyMessage;
    }
}

public class MarketClosedException : Exception
{
    public string UserFriendlyMessage { get; }

    public MarketClosedException(string message, string userFriendlyMessage)
        : base(message)
    {
        UserFriendlyMessage = userFriendlyMessage;
    }
}

/// <summary>
/// Service for fetching historical candle data with caching, fallback, and sub-minute interpolation.
/// </summary>
public class MarketDataFetcher
{
    public static MarketDataFetcher Instance { get; set; } = new MarketDataFetcher();
    private static readonly HttpClient _httpClient = new HttpClient(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(15),
        EnableMultipleHttp2Connections = true
    }) { Timeout = TimeSpan.FromSeconds(15) };
    private static readonly IMemoryCache _cache = new MemoryCache(new MemoryCacheOptions());
    private static readonly ResiliencePipeline _retryPipeline = new ResiliencePipelineBuilder()
        .AddRetry(new Polly.Retry.RetryStrategyOptions
        {
            ShouldHandle = new PredicateBuilder().Handle<HttpRequestException>().Handle<TaskCanceledException>(),
            MaxRetryAttempts = 3,
            DelayGenerator = static args => new ValueTask<TimeSpan?>(TimeSpan.FromMilliseconds(500 * (args.AttemptNumber + 1)))
        })
        .Build();



    private void CacheMockOhlc(string asset, string interval, double[] prices)
    {
        if (prices.Length == 0) return;
        var ohlc = new MiniAppController.OhlcCandle[prices.Length];
        double volatility = prices.Length > 1 ? Math.Abs(prices[^1] - prices[0]) / prices.Length * 2.0 : 0.0001;
        
        long currentTimeMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        int intervalSec = TimeframeSeconds(interval);
        
        for (int i = 0; i < prices.Length; i++)
        {
            double open = i == 0 ? prices[i] : prices[i - 1];
            double close = prices[i];
            double high = Math.Max(open, close) + volatility;
            double low = Math.Min(open, close) - volatility;
            ohlc[i] = new MiniAppController.OhlcCandle(open, high, low, close, 1.0);
        }
        _cache.Set($"ohlc_{asset}_{interval}", ohlc, TimeSpan.FromMinutes(10));
    }

    public MiniAppController.OhlcCandle[]? GetOhlcCandles(string key) =>
        _cache.TryGetValue($"ohlc_{key}", out MiniAppController.OhlcCandle[]? v) ? v : null;

    public void SetOhlcCandles(string key, MiniAppController.OhlcCandle[] candles) =>
        _cache.Set($"ohlc_{key}", candles, TimeSpan.FromMinutes(10));

    public string IntervalMap(string tf) => tf.ToLower() switch
    {
        "s3" or "s5" or "s10" or "s15" or "s30" => "1m",
        "m1" => "1m", "m2" => "1m", "m3" => "3m",
        "m5" => "5m", "m15" => "15m", "m30" => "30m",
        "h1" => "1h", "h4" => "4h",
        "d1" => "1d", _ => "1m"
    };

    public int GetExpiryCandles(string tf) => tf.ToLower() switch
    {
        "s3" or "s5" or "s10" or "s15" or "s30" => 3,
        "m1" => 3,
        "m2" => 2,
        "m3" => 2,
        "m5" => 3,
        "m15" => 2,
        "m30" => 2,
        "h1" => 2,
        "h4" => 1,
        "d1" => 1,
        _ => 3
    };

    public int TimeframeSeconds(string tf) => tf.ToLower() switch
    {
        "s3" => 3, "s5" => 5, "s10" => 10, "s15" => 15, "s30" => 30,
        "m1" => 60, "m2" => 120, "m3" => 180, "m5" => 300,
        "m15" => 900, "m30" => 1800,
        "h1" => 3600, "h4" => 14400,
        "d1" => 86400, _ => 60
    };

    public string? HigherTf(string tf) => tf.ToLower() switch
    {
        "s3" or "s5" or "s10" or "s15" or "s30" => "m5",
        "m1" => "m5", "m2" => "m5", "m3" => "m5",
        "m5" => "m15", "m15" => "h1", "m30" => "h1",
        "h1" => "h4", "h4" => "d1", _ => null
    };

    public string? LowerTf(string tf) => tf.ToLower() switch
    {
        "s10" or "s15" or "s30" => "s5",
        "s5" => "s3",
        "m1" => "s30",
        "m2" => "m1", "m3" => "m1",
        "m5" => "m1", "m15" => "m5", "m30" => "m15",
        "h1" => "m30", "h4" => "h1",
        "d1" => "h4", _ => null
    };

    public async Task<(double[] prices, double[] volumes)> FetchBinanceCandles(string symbol, string interval, int limit = 50, string? rawAsset = null, string? rawInterval = null)
    {
        string url = $"https://api.binance.com/api/v3/klines?symbol={symbol}&interval={interval}&limit={limit}";
        
        var _numOpts = new JsonSerializerOptions { NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString };
        var arr = await _retryPipeline.ExecuteAsync(async ct => await System.Net.Http.Json.HttpClientJsonExtensions.GetFromJsonAsync<double[][]>(_httpClient, url, _numOpts, ct));
        
        if (arr == null || arr.Length == 0) return (Array.Empty<double>(), Array.Empty<double>());

        var lastCandle = arr[^1];
        long openTimeMs = (long)lastCandle[0];
        var openTime = DateTimeOffset.FromUnixTimeMilliseconds(openTimeMs).UtcDateTime;

        if (DateTime.UtcNow - openTime > TimeSpan.FromDays(5))
        {
            throw new Exception($"Binance symbol {symbol} data is extremely stale ({openTime}).");
        }

        var prices = new double[arr.Length];
        var volumes = new double[arr.Length];
        var ohlc = new MiniAppController.OhlcCandle[arr.Length];

        for (int i = 0; i < arr.Length; i++)
        {
            var k = arr[i];
            double open = k[1];
            double high = k[2];
            double low = k[3];
            double close = k[4];
            double volume = k[5];

            prices[i] = close;
            volumes[i] = volume;
            ohlc[i] = new MiniAppController.OhlcCandle(open, high, low, close, volume);
        }

        if (prices.Length > 0)
        {
            string cacheSym = rawAsset ?? symbol;
            string cacheInt = rawInterval ?? interval;
            _cache.Set($"ohlc_{cacheSym}_{cacheInt}", ohlc, TimeSpan.FromMinutes(10));
        }

        return (prices, volumes);
    }

    public async Task<(double[] prices, double[] volumes)> FetchBinanceWithFallback(string? symbol, string rawInterval, string? originalAsset = null, int limit = 50, int cacheTtlSeconds = 10)
    {
        string interval = IntervalMap(rawInterval);
        if (symbol != null)
        {
            if (BinanceWebSocketStream.TryGetLiveCandles(symbol, interval, out var wsPrices, out var wsVolumes, out int count) && count >= 15)
            {
                try
                {
                    double[] exactPrices = new double[count];
                    double[] exactVolumes = new double[count];
                    Array.Copy(wsPrices, exactPrices, count);
                    Array.Copy(wsVolumes, exactVolumes, count);
                    
                    BotLogger.Info($"[MarketDataFetcher] Served live WebSocket candles for {symbol} ({interval}) in 0ms.");
                    CacheMockOhlc(originalAsset ?? symbol, rawInterval, exactPrices);
                    return (exactPrices, exactVolumes);
                }
                finally
                {
                    System.Buffers.ArrayPool<double>.Shared.Return(wsPrices);
                    System.Buffers.ArrayPool<double>.Shared.Return(wsVolumes);
                }
            }

            string binanceCacheKey = $"binance_raw_{symbol}_{interval}_{limit}";
            if (cacheTtlSeconds > 0 && _cache.TryGetValue(binanceCacheKey, out object? cachedVal) && cachedVal is ValueTuple<double[], double[]> cachedTuple)
            {
                return cachedTuple;
            }
        }

        if (symbol == null)
        {
            if (originalAsset != null)
            {


                // 3. Persistent Zero-Latency WebSocket Stream RAM cache
                if (TwelveDataWebSocketStream.TryGetRealtimePricesRented(originalAsset, out var wsTicks, out int wsCount, limit) && wsCount >= 15)
                {
                    try
                    {
                        double[] exactPrices = new double[wsCount];
                        Array.Copy(wsTicks, exactPrices, wsCount);
                        
                        double[] mockVolumes = new double[wsCount];
                        for (int i = 0; i < wsCount; i++) mockVolumes[i] = 1.0 + (i % 3) * 0.5;
                        BotLogger.Info($"[MarketDataFetcher] Served Zero-Latency Forex Persistent WebSocket ticks for {originalAsset} in 1ms.");
                        CacheMockOhlc(originalAsset, rawInterval, exactPrices);
                        return (exactPrices, mockVolumes);
                    }
                    finally
                    {
                        System.Buffers.ArrayPool<double>.Shared.Return(wsTicks);
                    }
                }

                var tdResult = await TwelveDataService.FetchCandlesAsync(originalAsset, interval, limit, cacheTtlSeconds);
                if (tdResult != null)
                    return tdResult.Value;
            }

            // Fallback to Binance ticker if TwelveData API key is not configured
            string cleanAsset = AssetSanitizer.Sanitize(originalAsset ?? "BTCUSDT");
            symbol = cleanAsset switch
            {
                "BTCUSDT" or "BTC" or "BTCUSD" => "BTCUSDT",
                "ETHUSDT" or "ETH" or "ETHUSD" => "ETHUSDT",
                "SOLUSDT" or "SOL" or "SOLUSD" => "SOLUSDT",
                _ => "BTCUSDT"
            };
        }

        try
        {
            var res = await FetchBinanceCandles(symbol, interval, limit, originalAsset ?? symbol, rawInterval);
            if (cacheTtlSeconds > 0 && res.prices.Length > 0)
            {
                string binanceCacheKey = $"binance_raw_{symbol}_{rawInterval}_{limit}";
                _cache.Set(binanceCacheKey, res, TimeSpan.FromSeconds(cacheTtlSeconds));
            }
            return res;
        }
        catch
        {
            if (originalAsset != null)
            {
                var tdResult = await TwelveDataService.FetchCandlesAsync(originalAsset, interval, limit, cacheTtlSeconds);
                if (tdResult != null)
                {
                    return tdResult.Value;
                }
            }

            throw new ExchangeUnavailableException($"Fallback blocked for {originalAsset ?? symbol}", $"⚠️ Данные для {originalAsset ?? symbol} временно недоступны на бирже.");
        }
    }
}






