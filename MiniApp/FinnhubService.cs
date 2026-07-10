using System.Text.Json;

namespace ValutaBot.MiniApp;

public static class FinnhubService
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private static string? _apiKey;

    private static string GetApiKey()
    {
        _apiKey ??= Environment.GetEnvironmentVariable("FINNHUB_API_KEY") ?? "";
        return _apiKey;
    }

    public static (double[] prices, double[] volumes)? FetchCandles(string rawAsset, string interval, int limit = 100)
    {
        string apiKey = GetApiKey();
        if (string.IsNullOrEmpty(apiKey)) return null;

        string? symbol = ConvertToFinnhubSymbol(rawAsset);
        string? resolution = ConvertResolution(interval);
        if (symbol == null || resolution == null) return null;

        try
        {
            string url = $"https://finnhub.io/api/v1/forex/candle?symbol={symbol}&resolution={resolution}&count={limit}&token={apiKey}";
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd("ValutaBot/1.0");

            var response = _http.Send(request);
            string body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            // Finnhub returns {"s": "ok"} on success, {"s": "no_data"} on failure
            string status = root.TryGetProperty("s", out var s) ? s.GetString() ?? "" : "";
            if (status != "ok")
            {
                Console.WriteLine($"[Finnhub] API error for {rawAsset}: status={status}");
                return null;
            }

            if (!root.TryGetProperty("c", out var closes) || !root.TryGetProperty("v", out var volumes))
                return null;

            var cArr = closes.EnumerateArray().Select(v => v.GetDouble()).ToArray();
            var vArr = volumes.EnumerateArray().Select(v => v.GetDouble()).ToArray();

            if (cArr.Length < 10) return null;

            return (cArr, vArr);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Finnhub] Fetch failed for {rawAsset}: {ex.Message}");
            return null;
        }
    }

    private static string? ConvertToFinnhubSymbol(string raw)
    {
        // Skip crypto — Finnhub forex endpoint doesn't cover crypto
        if (raw.Contains("BTC") || raw.Contains("ETH") || raw.Contains("SOL") || raw.Contains("USDT"))
            return null;

        string a = raw.Replace(" OTC", "").Trim().ToUpper();

        // Handle XAU/USD, XAG/USD
        if (a == "GOLD" || a == "XAUUSD") return "OANDA:XAU_USD";
        if (a == "SILVER" || a == "XAGUSD") return "OANDA:XAG_USD";

        // Handle "EUR/USD" format → "OANDA:EUR_USD"
        if (a.Contains("/"))
        {
            var parts = a.Split('/');
            if (parts.Length == 2)
            {
                string left = parts[0].Trim();
                string right = parts[1].Trim();
                if (left == "USD") return $"OANDA:{right}_{left}";
                return $"OANDA:{left}_{right}";
            }
        }

        // Handle "EURUSD" format (6 chars)
        if (a.Length == 6)
        {
            int split = a.Length / 2;
            return $"OANDA:{a[..split]}_{a[split..]}";
        }

        return null;
    }

    private static string? ConvertResolution(string interval) => interval.ToLower() switch
    {
        "1m" => "1",
        "5m" => "5",
        "15m" => "15",
        "30m" => "30",
        "1h" or "h1" => "60",
        "4h" or "h4" => "240",
        "1d" or "d1" => "D",
        "1w" or "w1" => "W",
        "1m" or "m1" => "M",
        _ => null
    };
}
