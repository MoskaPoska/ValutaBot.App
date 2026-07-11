using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace ValutaBot.MiniApp;

public static class NewsAnalysisService
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(8) };
    private static readonly ConcurrentDictionary<string, (double score, string sentiment, string summary, string[] headlines, long cachedAt)> _cache = new();

    public static (double score, string sentiment, string summary, string[] headlines) Analyze(string asset)
    {
        string key = asset.ToUpper().Trim();
        if (_cache.TryGetValue(key, out var cached) && DateTimeOffset.UtcNow.ToUnixTimeSeconds() - cached.cachedAt < 300)
            return (cached.score, cached.sentiment, cached.summary, cached.headlines);

        try
        {
            string query = BuildSearchQuery(asset);
            var headlines = FetchNewsHeadlines(query);
            if (headlines.Count == 0)
            {
                var fallback = FallbackResult();
                _cache[key] = (fallback.score, fallback.sentiment, fallback.summary, fallback.headlines, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                return fallback;
            }

            var (score, sentiment, summary) = AnalyzeWithLlm(headlines);
            var result = (score, sentiment, summary, headlines: headlines.Take(5).ToArray());
            _cache[key] = (result.score, result.sentiment, result.summary, result.headlines, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            return result;
        }
        catch
        {
            var fallback = FallbackResult();
            _cache[key] = (fallback.score, fallback.sentiment, fallback.summary, fallback.headlines, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            return fallback;
        }
    }

    private static string BuildSearchQuery(string asset)
    {
        string a = asset.ToUpper().Trim();
        if (a.Contains("BTC") || a.Contains("BITCOIN")) return "Bitcoin BTC crypto";
        if (a.Contains("ETH") || a.Contains("ETHEREUM")) return "Ethereum ETH crypto";
        if (a.Contains("SOL") || a.Contains("SOLANA")) return "Solana SOL crypto";
        if (a.Contains("XRP")) return "XRP Ripple crypto";
        if (a.Contains("ADA")) return "Cardano ADA crypto";
        if (a.Contains("DOGE")) return "Dogecoin DOGE crypto";
        if (a.Contains("DOT")) return "Polkadot DOT crypto";
        if (a.Contains("BNB")) return "BNB Binance crypto";
        if (a.Contains("EUR") && a.Contains("USD")) return "EUR/USD forex analysis";
        if (a.Contains("GBP") && a.Contains("USD")) return "GBP/USD forex analysis";
        if (a.Contains("USD") && a.Contains("JPY")) return "USD/JPY forex analysis";
        if (a.Contains("EUR") && a.Contains("JPY")) return "EUR/JPY forex analysis";
        if (a.Contains("GBP") && a.Contains("JPY")) return "GBP/JPY forex analysis";
        if (a.Contains("AUD") && a.Contains("USD")) return "AUD/USD forex analysis";
        if (a.Contains("AUD") && a.Contains("CAD")) return "AUD/CAD forex analysis";
        if (a.Contains("USD") && a.Contains("CAD")) return "USD/CAD forex analysis";
        if (a.Contains("USD") && a.Contains("CHF")) return "USD/CHF forex analysis";
        if (a.Contains("CAD") && a.Contains("CHF")) return "CAD/CHF forex analysis";
        if (a.Contains("EUR") && a.Contains("CHF")) return "EUR/CHF forex analysis";
        if (a.Contains("EUR") && a.Contains("NZD")) return "EUR/NZD forex analysis";
        if (a.Contains("NZD") && a.Contains("USD")) return "NZD/USD forex analysis";
        if (a.Contains("NZD") && a.Contains("JPY")) return "NZD/JPY forex analysis";
        if (a.Contains("EUR") && a.Contains("GBP")) return "EUR/GBP forex analysis";
        if (a.Contains("USD") && a.Contains("BRL")) return "USD/BRL forex analysis";
        if (a.Contains("USD") && a.Contains("IDR")) return "USD/IDR forex analysis";
        if (a.Contains("USD") && a.Contains("PKR")) return "USD/PKR forex analysis";
        if (a.Contains("USD") && a.Contains("DZD")) return "USD/DZD forex analysis";
        if (a.Contains("NGN") && a.Contains("USD")) return "NGN/USD forex analysis";
        if (a.Contains("LBP") && a.Contains("USD")) return "LBP/USD forex analysis";
        if (a.Contains("TND") && a.Contains("USD")) return "TND/USD forex analysis";
        if (a.Contains("JOD") && a.Contains("CNY")) return "JOD/CNY forex analysis";
        if (a.Contains("OMR") && a.Contains("CNY")) return "OMR/CNY forex analysis";
        if (a.Contains("SAR") && a.Contains("CNY")) return "SAR/CNY forex analysis";
        if (a.Contains("GOLD") || a.Contains("PAXG")) return "Gold XAU commodities";
        if (a.Contains("SILVER") || a.Contains("XAG")) return "Silver commodities";
        if (a.Contains("OIL") || a.Contains("BRENT")) return "Crude oil commodities";
        if (a.Contains("AAPL")) return "Apple AAPL stock";
        if (a.Contains("TSLA")) return "Tesla TSLA stock";
        if (a.Contains("AMZN")) return "Amazon AMZN stock";
        if (a.Contains("GOOGL") || a.Contains("GOOG")) return "Google GOOGL stock";
        if (a.Contains("MSFT")) return "Microsoft MSFT stock";
        return a.Replace("OTC", "").Trim() + " financial markets";
    }

    private static List<string> FetchNewsHeadlines(string query)
    {
        var headlines = new List<string>();
        try
        {
            string url = $"https://news.google.com/rss/search?q={Uri.EscapeDataString(query)}&hl=en-US&gl=US&ceid=US:en";
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
            var response = _http.Send(request);
            string xml = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            var doc = XDocument.Parse(xml);
            var items = doc.Descendants("item").Take(10);
            foreach (var item in items)
            {
                var title = item.Element("title")?.Value?.Trim();
                if (!string.IsNullOrEmpty(title))
                    headlines.Add(title);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[News] RSS fetch failed: {ex.Message}");
        }

        return headlines;
    }

    private static (double score, string sentiment, string summary) AnalyzeWithLlm(List<string> headlines)
    {
        return (0, "Нейтральной", "Анализ новостей отключен");
    }

    private static (double score, string sentiment, string summary) AnalyzeWithLlm_Disabled(List<string> headlines)
    {
        string apiKey = Environment.GetEnvironmentVariable("OpenAiApiKey") ?? "";
        if (string.IsNullOrEmpty(apiKey))
        {
            Console.WriteLine("[News] No OpenAI API key");
            return (0, "Нейтральной", "Ключ OpenAI не настроен");
        }

        try
        {
            string joined = string.Join("\n", headlines.Take(7).Select(h => "- " + h));
            var systemMsg = "You are a financial news analyst. Analyze the sentiment of these headlines for the asset. "
                          + "Respond in Russian with ONLY valid JSON: {\"score\": -2 to 2, \"sentiment\": \"бычий\"/\"медвежий\"/\"нейтральный\", \"summary\": \"1 sentence\"}";
            var userMsg = $"News headlines:\n{joined}";

            var body = new
            {
                model = "gpt-4o-mini",
                messages = new[]
                {
                    new { role = "system", content = systemMsg },
                    new { role = "user", content = userMsg }
                },
                temperature = 0.3,
                max_tokens = 200
            };

            var json = JsonSerializer.Serialize(body);
            var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions")
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

            var response = _http.Send(request);
            string responseBody = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            using var doc = JsonDocument.Parse(responseBody);

            string content = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString() ?? "{}";

            content = content.Trim();
            if (content.StartsWith("```")) content = content.Substring(content.IndexOf('\n') + 1);
            if (content.EndsWith("```")) content = content.Substring(0, content.LastIndexOf("```"));
            content = content.Trim();

            using var resultDoc = JsonDocument.Parse(content);
            var root = resultDoc.RootElement;
            double score = root.TryGetProperty("score", out var s) ? s.GetDouble() : 0;
            string sentiment = root.TryGetProperty("sentiment", out var sen) ? sen.GetString() ?? "нейтральный" : "нейтральный";
            string summary = root.TryGetProperty("summary", out var sum) ? sum.GetString() ?? "" : "";

            score = Math.Clamp(score, -2, 2);
            return (score, sentiment, summary);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[News] LLM analysis failed: {ex.Message}");
            return (0, "Нейтральной", "Ошибка анализа");
        }
    }

    private static (double score, string sentiment, string summary, string[] headlines) FallbackResult()
    {
        return (0, "Нейтральной", "Новости не найдены", Array.Empty<string>());
    }
}
