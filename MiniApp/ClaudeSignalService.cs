using System.Text;
using System.Text.Json;

namespace ValutaBot.MiniApp;

public static class ClaudeSignalService
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private static string? _lastRawResponse;

    public static string? GetLastRawResponse() => _lastRawResponse;

    public static string GetOpenRouterApiKey()
    {
        string apiKey = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY") 
            ?? Environment.GetEnvironmentVariable("OpenRouterApiKey") ?? "";

        if (string.IsNullOrEmpty(apiKey))
        {
            try
            {
                string settingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
                if (File.Exists(settingsPath))
                {
                    var json = File.ReadAllText(settingsPath);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("OpenRouterApiKey", out var prop))
                    {
                        apiKey = prop.GetString() ?? "";
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Claude] Failed to read appsettings.json: {ex.Message}");
            }
        }

        return (apiKey ?? "").Trim();
    }

    public static (string direction, double probability, string reasoning) AnalyzeSignal(
        string asset, double[] prices, double[] volumes,
        double rsi, double ema, double macd, double macdSignal,
        double adx, double bbZ, double volStrength, double imbalance)
    {

        try
        {
            string apiKey = GetOpenRouterApiKey();
            if (string.IsNullOrEmpty(apiKey))
            {
                Console.WriteLine("[Claude] No API key configured");
                return ("NEUTRAL", 50, "Ключ OpenRouter не настроен");
            }
            
            string maskedKey = apiKey.Length > 8 
                ? $"{apiKey.Substring(0, 6)}...{apiKey.Substring(apiKey.Length - 4)}" 
                : "***";
            Console.WriteLine($"[Claude] Using API key: {maskedKey} (length: {apiKey.Length})");

            double change = prices.Length >= 2
                ? (prices[^1] - prices[0]) / prices[0] * 100 : 0;

            double mom3 = prices.Length >= 4
                ? (prices[^1] - prices[^4]) / prices[^4] * 100 : 0;

            double mom5 = prices.Length >= 6
                ? (prices[^1] - prices[^6]) / prices[^6] * 100 : 0;

            double volatility = 0;
            for (int i = 1; i < prices.Length; i++)
                volatility += Math.Abs(prices[i] - prices[i - 1]);
            volatility /= prices.Length;

            string indicators = JsonSerializer.Serialize(new
            {
                asset,
                current_price = Math.Round(prices[^1], 5),
                change_percent = Math.Round(change, 2),
                mom3_percent = Math.Round(mom3, 3),
                mom5_percent = Math.Round(mom5, 3),
                rsi_14 = Math.Round(rsi, 1),
                ema_9 = Math.Round(ema, 5),
                macd = Math.Round(macd, 6),
                macd_signal = Math.Round(macdSignal, 6),
                adx_14 = Math.Round(adx, 1),
                bollinger_zscore = Math.Round(bbZ, 2),
                volume_strength = Math.Round(volStrength, 2),
                bid_ask_imbalance = Math.Round(imbalance, 3),
                volatility_per_candle = Math.Round(volatility, 6),
                high_52w = Math.Round(prices.Max(), 5),
                low_52w = Math.Round(prices.Min(), 5)
            });

            string systemPrompt = "You are a professional quantitative analyst with 20 years of experience. "
                + "Analyze the technical indicators below and predict the most likely short-term price direction. "
                + "Respond in Russian with ONLY valid JSON (no markdown, no code blocks): "
                + "{\"direction\": \"BUY\" or \"PUT\" or \"NEUTRAL\", "
                + "\"probability\": 55-95, "
                + "\"reasoning\": \"1-2 sentences explaining key signals\"}";

            string model = "anthropic/claude-sonnet-5";
            try
            {
                Console.WriteLine($"[Claude] Attempting request to OpenRouter with model: {model}");
                var body = new
                {
                    model = model,
                    messages = new[]
                    {
                        new { role = "system", content = systemPrompt },
                        new { role = "user", content = $"Technical indicators for {asset}:\n{indicators}" }
                    },
                    temperature = 0.2,
                    max_tokens = 300
                };

                var json = JsonSerializer.Serialize(body);
                var request = new HttpRequestMessage(HttpMethod.Post, "https://openrouter.ai/api/v1/chat/completions")
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
                request.Headers.Add("HTTP-Referer", "https://valutabotapp-production.up.railway.app");
                request.Headers.Add("X-Title", "ValutaBot");

                var response = _http.Send(request);
                string responseBody = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                _lastRawResponse = responseBody;

                if (!response.IsSuccessStatusCode)
                {
                    throw new Exception($"HTTP {(int)response.StatusCode}: {responseBody}");
                }

                using var doc = JsonDocument.Parse(responseBody);
                if (doc.RootElement.TryGetProperty("error", out var errProp))
                {
                    string errMsg = errProp.TryGetProperty("message", out var msgProp) ? msgProp.GetString() ?? "" : "";
                    throw new Exception($"OpenRouter error: {errMsg}");
                }

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
                string direction = root.TryGetProperty("direction", out var d) ? d.GetString() ?? "NEUTRAL" : "NEUTRAL";
                double probability = root.TryGetProperty("probability", out var p) ? p.GetDouble() : 50;
                string reasoning = root.TryGetProperty("reasoning", out var r) ? r.GetString() ?? "" : "";

                direction = direction.ToUpper() switch { "BUY" => "BUY", "SELL" => "PUT", "PUT" => "PUT", _ => "NEUTRAL" };
                probability = Math.Clamp(probability, 50, 98);

                return (direction, probability, reasoning);
            }
            catch (Exception ex)
            {
                throw new Exception($"Request failed: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Claude] Analysis failed: {ex.Message}");
            _lastRawResponse = $"ERROR: {ex.Message}";
            return ("NEUTRAL", 50, $"Ошибка запроса к Claude 3.5 Sonnet: {ex.Message}");
        }
    }
}
