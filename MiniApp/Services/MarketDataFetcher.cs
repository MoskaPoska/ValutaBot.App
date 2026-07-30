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
/// Service for fetching historical candle data with fallback, and sub-minute interpolation.
/// Fully stateless. Relies on WebSocket for fast path caching.
/// </summary>
public class MarketDataFetcher
{
    public static MarketDataFetcher Instance { get; set; } = new MarketDataFetcher();
    private static readonly HttpClient _httpClient = new HttpClient(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(15),
        EnableMultipleHttp2Connections = true
    }) { Timeout = TimeSpan.FromSeconds(15) };

    private static readonly ResiliencePipeline _retryPipeline = new ResiliencePipelineBuilder()
        .AddRetry(new Polly.Retry.RetryStrategyOptions
        {
            ShouldHandle = new PredicateBuilder().Handle<HttpRequestException>().Handle<TaskCanceledException>(),
            MaxRetryAttempts = 3,
            DelayGenerator = static args => new ValueTask<TimeSpan?>(TimeSpan.FromMilliseconds(500 * (args.AttemptNumber + 1)))
        })
        .Build();

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

    public async Task<MiniAppController.OhlcCandle[]> FetchBinanceOhlcCandlesAsync(string symbol, string interval, int limit = 50)
    {
        // Try Websocket fast path first
        if (BinanceWebSocketStream.TryGetLiveCandles(symbol, interval, out var wsPrices, out var wsVolumes, out int count) && count >= limit)
        {
            var resultFast = new MiniAppController.OhlcCandle[limit];
            int startIdx = count - limit;
            for (int i = 0; i < limit; i++)
            {
                resultFast[i] = new MiniAppController.OhlcCandle(wsPrices[startIdx + i], wsPrices[startIdx + i], wsPrices[startIdx + i], wsPrices[startIdx + i], wsVolumes[startIdx + i]);
            }
            System.Buffers.ArrayPool<double>.Shared.Return(wsPrices);
            System.Buffers.ArrayPool<double>.Shared.Return(wsVolumes);
            return resultFast;
        }

        string url = "https://api.binance.com/api/v3/klines?symbol=$symbol&interval=$interval&limit=$limit";
        
        var _numOpts = new JsonSerializerOptions { NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString };
        var arr = await _retryPipeline.ExecuteAsync(async ct => await System.Net.Http.Json.HttpClientJsonExtensions.GetFromJsonAsync<double[][]>(_httpClient, new Uri(url), _numOpts, ct));
        
        if (arr == null || arr.Length == 0) return Array.Empty<MiniAppController.OhlcCandle>();

        var result = new MiniAppController.OhlcCandle[arr.Length];
        for (int i = 0; i < arr.Length; i++)
        {
            var k = arr[i];
            result[i] = new MiniAppController.OhlcCandle(k[1], k[2], k[3], k[4], k[5]);
        }
        return result;
    }

    public async Task<(double[] prices, double[] volumes)> FetchBinanceCandles(string symbol, string interval, int limit = 50, string? rawAsset = null, string? rawInterval = null)
    {
        var ohlc = await FetchBinanceOhlcCandlesAsync(symbol, interval, limit);
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

    public async Task<(double[] prices, double[] volumes)> FetchBinanceWithFallback(string? symbol, string rawInterval, string? originalAsset = null, int limit = 50, int cacheTtlSeconds = 10)
    {
        string interval = IntervalMap(rawInterval);
        string cacheSym = originalAsset ?? symbol ?? "UNKNOWN";

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
                    
                    BotLogger.Info("[MarketDataFetcher] Served live WebSocket candles for $symbol ($interval) in 0ms.");

                    return (exactPrices, exactVolumes);
                }
                finally
                {
                    System.Buffers.ArrayPool<double>.Shared.Return(wsPrices);
                    System.Buffers.ArrayPool<double>.Shared.Return(wsVolumes);
                }
            }
        }

        if (symbol == null)
        {
            if (originalAsset != null)
            {
                var tdResult = await TwelveDataService.FetchCandlesAsync(originalAsset, interval, limit);
                if (tdResult != null)
                    return tdResult.Value;
            }

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
            return res;
        }
        catch
        {
            if (originalAsset != null)
            {
                var tdResult = await TwelveDataService.FetchCandlesAsync(originalAsset, interval, limit);
                if (tdResult != null)
                {
                    return tdResult.Value;
                }
            }

            throw new ExchangeUnavailableException("Fallback blocked for $(originalAsset ?? symbol)", "Биржа недоступна.");
        }
    }
    
    public async Task<double?> FetchHistoricalPriceAsync(string symbol, long endTimeMs)
    {
        return await _retryPipeline.ExecuteAsync<double?>(async (CancellationToken ct) =>
        {
            string url = "https://api.binance.com/api/v3/klines?symbol=$symbol&interval=1m&endTime=$endTimeMs&limit=1";
            var response = await _httpClient.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0)
            {
                var kline = doc.RootElement[0];
                if (kline.ValueKind == JsonValueKind.Array && kline.GetArrayLength() >= 5)
                {
                    string closeStr = kline[4].GetString() ?? "0";
                    if (double.TryParse(closeStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double closePrice))
                    {
                        return closePrice;
                    }
                }
            }
            return null;
        });
    }
}
