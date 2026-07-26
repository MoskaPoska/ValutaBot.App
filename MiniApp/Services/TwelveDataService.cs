using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;

namespace ValutaBot.MiniApp;

public static partial class TwelveDataService
{
    private static readonly HttpClient _http = new(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(15),
        EnableMultipleHttp2Connections = true
    }) { Timeout = TimeSpan.FromSeconds(10) };
    private static readonly IMemoryCache _memoryCache = new MemoryCache(new MemoryCacheOptions());
    private static string? _apiKey;

    private static readonly ConcurrentQueue<DateTime> _apiCallTimestamps = new();
    private static readonly object _rateLimitLock = new();

    private static bool CheckAndRegisterRateLimit()
    {
        lock (_rateLimitLock)
        {
            DateTime now = DateTime.UtcNow;
            while (_apiCallTimestamps.TryPeek(out var oldest) && (now - oldest).TotalSeconds > 60)
            {
                _apiCallTimestamps.TryDequeue(out _);
            }

            // TwelveData rate limit is 8 requests per minute. We limit to 7 for safety.
            if (_apiCallTimestamps.Count >= 7)
            {
                return false;
            }

            _apiCallTimestamps.Enqueue(now);
            return true;
        }
    }

    public static string GetApiKey()
    {
        _apiKey ??= Environment.GetEnvironmentVariable("TwelveDataApiKey") ?? "";
        return _apiKey;
    }

    public static async Task<(double[] prices, double[] volumes)?> FetchCandlesAsync(string rawAsset, string interval, int limit = 100, int cacheTtlSeconds = 10)
    {
        string key = $"TWELVE_DATA_{rawAsset.ToUpper()}_{interval.ToLower()}";

        // 1. Check IMemoryCache first for fresh data
        if (cacheTtlSeconds > 0 && _memoryCache.TryGetValue(key, out (double[] prices, double[] volumes) cachedData))
        {
            BotLogger.Info($"[TwelveData] Using IMemoryCache data for {rawAsset} ({interval})");
            return cachedData;
        }

        string apiKey = GetApiKey();
        if (string.IsNullOrEmpty(apiKey)) return null;

        // 2. Check rolling rate limiter before making the HTTP API call
        if (!CheckAndRegisterRateLimit())
        {
            if (_memoryCache.TryGetValue(key, out (double[] prices, double[] volumes) lastData))
            {
                BotLogger.Info($"[TwelveData] Rate limit safety triggered. Serving IMemoryCache for {rawAsset} ({interval}).");
                return lastData;
            }
            BotLogger.Warn($"[TwelveData] Rate limit safety triggered, but no cache exists for {rawAsset} ({interval})!");
            return null;
        }

        try
        {
            string symbol = ConvertToTwelveSymbol(rawAsset) ?? "";
            string tdInterval = ConvertInterval(interval) ?? "";
            if (string.IsNullOrEmpty(symbol) || string.IsNullOrEmpty(tdInterval)) return null;

            string url = $"https://api.twelvedata.com/time_series?symbol={Uri.EscapeDataString(symbol)}&interval={tdInterval}&outputsize={limit}&apikey={apiKey}";
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd("ValutaBot/1.0");

            var response = await _http.SendAsync(request);
            string body = await response.Content.ReadAsStringAsync();

            using var doc = JsonDocument.Parse(body);

            if (doc.RootElement.TryGetProperty("status", out var status) && status.GetString() == "error")
            {
                var msg = doc.RootElement.TryGetProperty("message", out var m) ? m.GetString() : "";
                BotLogger.Warn($"[TwelveData] API error for {rawAsset}: {msg}");
                throw new Exception($"TwelveData API error: {msg}");
            }

            if (!doc.RootElement.TryGetProperty("values", out var values))
            {
                if (_memoryCache.TryGetValue(key, out (double[] prices, double[] volumes) lastData))
                {
                    BotLogger.Warn($"[TwelveData] No values in response, serving IMemoryCache for {rawAsset}");
                    return lastData;
                }
                return null;
            }

            var arr = values.EnumerateArray().ToList();
            if (arr.Count < 10)
            {
                if (_memoryCache.TryGetValue(key, out (double[] prices, double[] volumes) lastData))
                {
                    BotLogger.Warn($"[TwelveData] Too few candles ({arr.Count}), serving IMemoryCache for {rawAsset}");
                    return lastData;
                }
                return null;
            }

            var prices = arr
                .Select(v => double.Parse(v.GetProperty("close").GetString()!, System.Globalization.CultureInfo.InvariantCulture))
                .Reverse()
                .ToArray();

            var volumes = arr
                .Select(v => v.TryGetProperty("volume", out var volProp) && double.TryParse(volProp.GetString(), System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var vol) ? vol : 0.0)
                .Reverse()
                .ToArray();

            try
            {
                var ohlc = arr.Select(v => new MiniAppController.OhlcCandle(
                    double.Parse(v.GetProperty("open").GetString()!, System.Globalization.CultureInfo.InvariantCulture),
                    v.TryGetProperty("high", out var h) ? double.Parse(h.GetString()!, System.Globalization.CultureInfo.InvariantCulture) : 0,
                    v.TryGetProperty("low", out var l) ? double.Parse(l.GetString()!, System.Globalization.CultureInfo.InvariantCulture) : 0,
                    double.Parse(v.GetProperty("close").GetString()!, System.Globalization.CultureInfo.InvariantCulture),
                    v.TryGetProperty("volume", out var vl) && double.TryParse(vl.GetString(), System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out var volVal) ? volVal : 0
                )).Reverse().ToArray();
                MiniAppController.SetOhlcCandles($"{rawAsset}_{interval}", ohlc);
            }
            catch (Exception ohlcEx)
            {
                BotLogger.Warn($"[TwelveData] OHLC cache parse warning for {rawAsset}", ohlcEx);
            }

            if (cacheTtlSeconds > 0)
            {
                _memoryCache.Set(key, (prices, volumes), TimeSpan.FromSeconds(cacheTtlSeconds));
            }
            BotLogger.Info($"[TwelveData] Successfully fetched {prices.Length} candles for {symbol} ({interval})");
            return (prices, volumes);
        }
        catch (JsonException jsonEx)
        {
            BotLogger.Warn($"[TwelveData] JSON parse error for {rawAsset}", jsonEx);

            if (_memoryCache.TryGetValue(key, out (double[] prices, double[] volumes) lastData))
            {
                BotLogger.Info($"[TwelveData] Serving IMemoryCache fallback data for {rawAsset}");
                return lastData;
            }
            return null;
        }
        catch (Exception ex)
        {
            BotLogger.Warn($"[TwelveData] Fetch failed for {rawAsset}: {ex.Message}");

            if (_memoryCache.TryGetValue(key, out (double[] prices, double[] volumes) lastData))
            {
                BotLogger.Info($"[TwelveData] Serving IMemoryCache fallback data for {rawAsset}");
                return lastData;
            }
            return null;
        }
    }

    public static string? ConvertToTwelveSymbol(string raw)
    {

        string original = raw.ToUpper()
            .Replace("OTC", "")
            .Replace("ОТС", "") // Cyrillic
            .Trim();

        if (original.Contains("GOLD") || original.Contains("XAUUSD")) return "XAU/USD";
        if (original.Contains("SILVER") || original.Contains("XAGUSD")) return "XAG/USD";

        string cleanTicker = original.Replace(" ", "").Replace("/", "").Replace("-", "").Replace("_", "");
        string[] knownStocks = { "AAPL", "TSLA", "AMZN", "GOOGL", "MSFT", "NVDA", "META" };
        if (knownStocks.Contains(cleanTicker))
        {
            return cleanTicker;
        }

        if (original.Contains("/"))
        {
            var parts = original.Split('/');
            if (parts.Length == 2)
            {
                string left = parts[0].Trim();
                string right = parts[1].Trim();
                return $"{left}/{right}";
            }
        }

        if (cleanTicker.Length == 6 || cleanTicker.Length == 7)
        {
            int split = cleanTicker.Length / 2;
            string left = cleanTicker[..split];
            string right = cleanTicker[split..];
            return $"{left}/{right}";
        }

        return null;
    }

    private static string? ConvertInterval(string interval) => interval.ToLower() switch
    {
        "1m" or "m1" => "1min",
        "2m" or "m2" => "2min",
        "3m" or "m3" => "3min",
        "5m" or "m5" => "5min",
        "15m" or "m15" => "15min",
        "30m" or "m30" => "30min",
        "45m" => "45min",
        "1h" or "h1" => "1h",
        "2h" or "h2" => "2h",
        "4h" or "h4" => "4h",
        "1d" or "d1" => "1day",
        _ => "1min"
    };

}
