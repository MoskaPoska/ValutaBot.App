using System.Collections.Concurrent;
using System.Text.Json;

namespace ValutaBot.MiniApp;

public static class TwelveDataService
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private static readonly ConcurrentDictionary<string, (double[] prices, double[] volumes, DateTime fetchedAt)> _cache = new();
    private static string? _apiKey;

    private static string GetApiKey()
    {
        _apiKey ??= Environment.GetEnvironmentVariable("TwelveDataApiKey") ?? "";
        return _apiKey;
    }

    public static (double[] prices, double[] volumes)? FetchCandles(string rawAsset, string interval, int limit = 100)
    {
        string key = $"{rawAsset}_{interval}";

        // 1. Check cache first for fresh data (less than 60 seconds old)
        if (_cache.TryGetValue(key, out var cached) && (DateTime.UtcNow - cached.fetchedAt).TotalSeconds < 60)
        {
            Console.WriteLine($"[TwelveData] Using cached data for {rawAsset} ({interval}) - fresh");
            return (cached.prices, cached.volumes);
        }

        string apiKey = GetApiKey();
        if (string.IsNullOrEmpty(apiKey)) return null;

        try
        {
            string symbol = ConvertToTwelveSymbol(rawAsset) ?? "";
            string tdInterval = ConvertInterval(interval) ?? "";
            if (string.IsNullOrEmpty(symbol) || string.IsNullOrEmpty(tdInterval)) return null;

            string url = $"https://api.twelvedata.com/time_series?symbol={Uri.EscapeDataString(symbol)}&interval={tdInterval}&outputsize={limit}&apikey={apiKey}";
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd("ValutaBot/1.0");

            var response = _http.Send(request);
            string body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

            using var doc = JsonDocument.Parse(body);

            if (doc.RootElement.TryGetProperty("status", out var status) && status.GetString() == "error")
            {
                var msg = doc.RootElement.TryGetProperty("message", out var m) ? m.GetString() : "";
                Console.WriteLine($"[TwelveData] API error: {msg}");

                if (_cache.TryGetValue(key, out var last))
                {
                    Console.WriteLine($"[TwelveData] Using last known data for {rawAsset}");
                    return (last.prices, last.volumes);
                }
                return null;
            }

            if (!doc.RootElement.TryGetProperty("values", out var values))
            {
                if (_cache.TryGetValue(key, out var last))
                {
                    Console.WriteLine($"[TwelveData] No values, using last known data for {rawAsset}");
                    return (last.prices, last.volumes);
                }
                return null;
            }

            var arr = values.EnumerateArray().ToList();
            if (arr.Count < 10)
            {
                if (_cache.TryGetValue(key, out var last))
                {
                    Console.WriteLine($"[TwelveData] Too few candles ({arr.Count}), using last known data for {rawAsset}");
                    return (last.prices, last.volumes);
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

            // Cache full OHLC for Claude pattern analysis
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
                // Use rawAsset_interval as key (matches what MiniAppController will look up)
                MiniAppController.SetOhlcCandles($"{rawAsset}_{interval}", ohlc);
            }
            catch (Exception ohlcEx)
            {
                Console.WriteLine($"[TwelveData] OHLC cache failed: {ohlcEx.Message}");
            }

            _cache[key] = (prices, volumes, DateTime.UtcNow);
            Console.WriteLine($"[TwelveData] Fetched {prices.Length} candles for {symbol} ({interval})");
            return (prices, volumes);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TwelveData] Fetch failed: {ex.Message}");

            if (_cache.TryGetValue(key, out var last))
            {
                Console.WriteLine($"[TwelveData] Using last known data for {rawAsset}");
                return (last.prices, last.volumes);
            }
            return null;
        }
    }

    private static string? ConvertToTwelveSymbol(string raw)
    {
        if (raw.Contains("BTC") || raw.Contains("ETH") || raw.Contains("SOL"))
            return null;

        string a = raw.Replace(" OTC", "").Trim().ToUpper();
        if (a == "GOLD" || a == "XAUUSD") return "XAU/USD";
        if (a == "SILVER" || a == "XAGUSD") return "XAG/USD";

        if (a.Contains("/"))
        {
            var parts = a.Split('/');
            if (parts.Length == 2)
            {
                string left = parts[0].Trim();
                string right = parts[1].Trim();

                if (left == "USD") return $"{right}/{left}";

                return a;
            }
        }

        if (a.Length == 6 || a.Length == 7)
        {
            int split = a.Length / 2;
            string left = a[..split];
            string right = a[split..];
            return $"{left}/{right}";
        }

        return null;
    }

    private static string? ConvertInterval(string interval) => interval.ToLower() switch
    {
        "1m" => "1min",
        "2m" => "2min",
        "3m" => "5min",
        "5m" => "5min",
        "15m" => "15min",
        "30m" => "30min",
        "45m" => "45min",
        "1h" or "h1" => "1h",
        "2h" or "h2" => "2h",
        "4h" or "h4" => "4h",
        "1d" or "d1" => "1day",
        _ => null
    };
}
