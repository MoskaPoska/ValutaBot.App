using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
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
/// Service for fetching historical candle data.
/// Now exclusively reads from the Redis LiveCandleAggregator.
/// </summary>
public class MarketDataFetcher
{
    public static MarketDataFetcher Instance { get; set; } = new MarketDataFetcher();

    public string IntervalMap(string tf) => tf.ToLower() switch
    {
        "s3" or "s5" or "s10" or "s15" or "s30" => "1m",
        "m1" => "1m", "m2" => "1m", "m3" => "3m",
        "m5" => "5m", "m15" => "15m", "m30" => "30m",
        "h1" => "1h", "h4" => "4h",
        "d1" => "1d", _ => "1m"
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

    public virtual async Task<MiniAppController.OhlcCandle[]> FetchOhlcWithFallbackAsync(string? symbol, string rawInterval, string? originalAsset = null, int limit = 50)
    {
        string interval = IntervalMap(rawInterval);
        
        string cleanAsset = AssetSanitizer.Sanitize(originalAsset ?? symbol ?? "EURUSDT");
        string finalSymbol = symbol ?? (cleanAsset switch
        {
            "EURUSD" or "EURUSDT" => "EURUSD_otc",
            "GBPUSD" or "GBPUSDT" => "GBPUSD_otc",
            "AUDUSD" or "AUDUSDT" => "AUDUSD_otc",
            _ => cleanAsset.EndsWith("_otc") ? cleanAsset : cleanAsset + "_otc"
        });
        
        var aggregator = Services.LiveCandleAggregator.Instance;
        if (aggregator == null)
        {
            BotLogger.Warn($"[MarketDataFetcher] LiveCandleAggregator is not initialized.");
            return Array.Empty<MiniAppController.OhlcCandle>();
        }

        var candles = aggregator.GetCandles(finalSymbol, interval, limit);
        if (candles.Length == 0)
        {
            BotLogger.Warn($"[MarketDataFetcher] No candles available yet in Redis for {finalSymbol} ({interval}).");
        }
        else
        {
            BotLogger.Info($"[MarketDataFetcher] Served {candles.Length} candles from Redis aggregator for {finalSymbol} ({interval}).");
        }

        return candles;
    }

    public virtual async Task<(double[] prices, double[] volumes)> FetchBinanceWithFallback(string? symbol, string rawInterval, string? originalAsset = null, int limit = 50)
    {
        var ohlc = await FetchOhlcWithFallbackAsync(symbol, rawInterval, originalAsset, limit);
        if (ohlc.Length == 0) return (Array.Empty<double>(), Array.Empty<double>());

        var prices = new double[ohlc.Length];
        var volumes = new double[ohlc.Length];

        for (int i = 0; i < ohlc.Length; i++)
        {
            prices[i] = ohlc[i].Close;
            volumes[i] = ohlc[i].Volume;
        }

        return (prices, volumes);
    }

    public async Task<double?> FetchHistoricalPriceAsync(string symbol, long endTimeMs)
    {
        // Not supported with pure Redis ticks, returning null so caller can ignore or fail gracefully
        return null;
    }
}
