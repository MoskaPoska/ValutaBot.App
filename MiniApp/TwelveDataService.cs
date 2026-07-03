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

    public static (double[] prices, double[] volumes)? FetchCandles(string rawAsset, string interval)
    {
        string key = $"{rawAsset}_{interval}";

        // 1. Check cache first for fresh data (less than 30 seconds old)
        if (_cache.TryGetValue(key, out var cached) && (DateTime.UtcNow - cached.fetchedAt).TotalSeconds < 30)
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

            string url = $"https://api.twelvedata.com/time_series?symbol={Uri.EscapeDataString(symbol)}&interval={tdInterval}&outputsize=50&apikey={apiKey}";
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
                .Select(v => double.TryParse(v.GetProperty("volume").GetString(), System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var vol) ? vol : 0)
                .Reverse()
                .ToArray();

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

        string a = raw.Replace(" OTC", "").Trim();
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
