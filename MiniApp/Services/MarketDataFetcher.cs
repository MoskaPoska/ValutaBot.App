using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Polly;
using Polly.Retry;

namespace ValutaBot.MiniApp;

/// <summary>
/// Service for fetching historical candle data with caching, fallback, and sub-minute interpolation.
/// </summary>
public static class MarketDataFetcher
{
    private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
    private static readonly IMemoryCache _cache = new MemoryCache(new MemoryCacheOptions());
    private static readonly AsyncRetryPolicy _retryPolicy = Policy
        .Handle<HttpRequestException>()
        .Or<TaskCanceledException>()
        .WaitAndRetryAsync(3, attempt => TimeSpan.FromMilliseconds(500 * attempt));

    private static readonly ConcurrentDictionary<string, MiniAppController.OhlcCandle[]> _ohlcCache = new();

    public static MiniAppController.OhlcCandle[]? GetOhlcCandles(string key) =>
        _ohlcCache.TryGetValue(key, out var v) ? v : null;

    public static void SetOhlcCandles(string key, MiniAppController.OhlcCandle[] candles) =>
        _ohlcCache[key] = candles;

    public static string IntervalMap(string tf) => tf.ToLower() switch
    {
        "s3" or "s5" or "s10" or "s15" or "s30" => "1m",
        "m1" => "1m", "m2" => "1m", "m3" => "3m",
        "m5" => "5m", "m15" => "15m", "m30" => "30m",
        "h1" => "1h", "h4" => "4h",
        "d1" => "1d", _ => "1m"
    };

    public static int GetExpiryCandles(string tf) => tf.ToLower() switch
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

    public static int TimeframeSeconds(string tf) => tf.ToLower() switch
    {
        "s3" => 3, "s5" => 5, "s10" => 10, "s15" => 15, "s30" => 30,
        "m1" => 60, "m2" => 120, "m3" => 180, "m5" => 300,
        "m15" => 900, "m30" => 1800,
        "h1" => 3600, "h4" => 14400,
        "d1" => 86400, _ => 60
    };

    public static string? HigherTf(string tf) => tf.ToLower() switch
    {
        "s3" or "s5" or "s10" or "s15" or "s30" => "m5",
        "m1" => "m5", "m2" => "m5", "m3" => "m5",
        "m5" => "m15", "m15" => "h1", "m30" => "h1",
        "h1" => "h4", "h4" => "d1", _ => null
    };

    public static string? LowerTf(string tf) => tf.ToLower() switch
    {
        "m1" => null,
        "m2" => "m1", "m3" => "m1",
        "m5" => "m1", "m15" => "m5", "m30" => "m15",
        "h1" => "m30", "h4" => "h1",
        "d1" => "h4", _ => null
    };

    public static async Task<(double[] prices, double[] volumes)> FetchBinanceCandles(string symbol, string interval, int limit = 50)
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

            if (DateTime.UtcNow - openTime > TimeSpan.FromDays(5))
            {
                throw new Exception($"Binance symbol {symbol} data is extremely stale ({openTime}).");
            }
        }

        var prices = arr.Select(k => double.Parse(k[4].GetString()!, CultureInfo.InvariantCulture)).ToArray();
        var volumes = arr.Select(k => double.Parse(k[5].GetString()!, CultureInfo.InvariantCulture)).ToArray();

        var ohlc = arr.Select(k => new MiniAppController.OhlcCandle(
            double.Parse(k[1].GetString()!, CultureInfo.InvariantCulture),
            double.Parse(k[2].GetString()!, CultureInfo.InvariantCulture),
            double.Parse(k[3].GetString()!, CultureInfo.InvariantCulture),
            double.Parse(k[4].GetString()!, CultureInfo.InvariantCulture),
            double.Parse(k[5].GetString()!, CultureInfo.InvariantCulture)
        )).ToArray();
        _ohlcCache[$"{symbol}_{interval}"] = ohlc;

        return (prices, volumes);
    }

    public static async Task<(double[] prices, double[] volumes)> FetchBinanceWithFallback(string? symbol, string interval, string? originalAsset = null, int limit = 50, int cacheTtlSeconds = 10)
    {
        if (symbol != null)
        {
            if (BinanceWebSocketStream.TryGetLiveCandles(symbol, interval, out var wsPrices, out var wsVolumes) && wsPrices.Length >= 15)
            {
                BotLogger.Info($"[MarketDataFetcher] Served live WebSocket candles for {symbol} ({interval}) in 0ms.");
                return (wsPrices, wsVolumes);
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
            if (originalAsset != null)
            {
                var tdResult = await TwelveDataService.FetchCandlesAsync(originalAsset, interval, limit, cacheTtlSeconds);
                if (tdResult != null)
                {
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
                _ => null
            };

            if (fallback != null)
            {
                var res = await FetchBinanceCandles(fallback, interval, limit);
                string binanceCacheKey = $"binance_raw_{symbol}_{interval}_{limit}";
                _cache.Set(binanceCacheKey, res, TimeSpan.FromSeconds(2));
                return res;
            }

            throw;
        }
    }
}
