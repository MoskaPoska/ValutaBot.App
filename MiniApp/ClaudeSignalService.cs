using System.Text;
using System.Text.Json;

namespace ValutaBot.MiniApp;

public static class ClaudeSignalService
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(60) };
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
                current_price = SafeRound(prices[^1], 5),
                change_percent = SafeRound(change, 2),
                mom3_percent = SafeRound(mom3, 3),
                mom5_percent = SafeRound(mom5, 3),
                rsi_14 = SafeRound(rsi, 1),
                ema_9 = SafeRound(ema, 5),
                macd = SafeRound(macd, 6),
                macd_signal = SafeRound(macdSignal, 6),
                adx_14 = SafeRound(adx, 1),
                bollinger_zscore = SafeRound(bbZ, 2),
                volume_strength = SafeRound(volStrength, 2),
                bid_ask_imbalance = SafeRound(imbalance, 3),
                volatility_per_candle = SafeRound(volatility, 6),
                local_high = SafeRound(prices.Max(), 5),
                local_low = SafeRound(prices.Min(), 5)
            });

            string systemPrompt = "You are a professional quantitative analyst with 20 years of experience. "
                + "Analyze the technical indicators below and predict the most likely short-term price direction. "
                + "Respond in Russian with ONLY valid JSON (no markdown, no code blocks): "
                + "{\"direction\": \"BUY\" or \"PUT\" or \"NEUTRAL\", "
                + "\"probability\": 55-95, "
                + "\"reasoning\": \"1-2 sentences explaining key signals\"}";

            string model = "google/gemini-2.5-pro";
            try
            {
                return SendOpenRouterRequest(model, apiKey, systemPrompt, asset, indicators);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Claude] Primary model {model} failed: {ex.Message}. Attempting fallback to Gemini 2.5 Flash...");
                try
                {
                    string fallbackModel = "google/gemini-2.5-flash";
                    var fallbackResult = SendOpenRouterRequest(fallbackModel, apiKey, systemPrompt, asset, indicators);
                    return (fallbackResult.direction, fallbackResult.probability, fallbackResult.reasoning + " (Gemini 2.5 Flash)");
                }
                catch (Exception fallbackEx)
                {
                    Console.WriteLine($"[Claude] Fallback model also failed: {fallbackEx.Message}");
                    throw new Exception($"Primary and fallback models failed. Gemini Pro: {ex.Message}. Gemini Flash: {fallbackEx.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Claude] Analysis failed: {ex.Message}");
            _lastRawResponse = $"ERROR: {ex.Message}";
            return ("NEUTRAL", 50, $"Ошибка запроса к AI: {ex.Message}");
        }
    }

    private static (string direction, double probability, string reasoning) SendOpenRouterRequest(
        string model, string apiKey, string systemPrompt, string asset, string indicators)
    {
        Console.WriteLine($"[Claude] Attempting request to OpenRouter with model: {model}");
        object body;
        if (model.Contains("o1-"))
        {
            // o1-mini does not support temperature/max_tokens or standard system role inside messages
            body = new Dictionary<string, object>
            {
                { "model", model },
                { "messages", new[]
                    {
                        new Dictionary<string, string> { { "role", "user" }, { "content", $"{systemPrompt}\n\nTechnical indicators for {asset}:\n{indicators}" } }
                    }
                }
            };
        }
        else
        {
            body = new Dictionary<string, object>
            {
                { "model", model },
                { "messages", new[]
                    {
                        new Dictionary<string, string> { { "role", "system" }, { "content", systemPrompt } },
                        new Dictionary<string, string> { { "role", "user" }, { "content", $"Technical indicators for {asset}:\n{indicators}" } }
                    }
                },
                { "temperature", 0.2 },
                { "max_tokens", 800 },
                { "max_completion_tokens", 800 }
            };
        }

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

        content = CleanJsonString(content);

        try
        {
            using var resultDoc = JsonDocument.Parse(content);
            var root = resultDoc.RootElement;
            string direction = root.TryGetProperty("direction", out var d) ? d.GetString() ?? "NEUTRAL" : "NEUTRAL";
            double probability = root.TryGetProperty("probability", out var p) ? p.GetDouble() : 50;
            string reasoning = root.TryGetProperty("reasoning", out var r) ? r.GetString() ?? "" : "";

            direction = direction.ToUpper() switch { "BUY" => "BUY", "SELL" => "PUT", "PUT" => "PUT", _ => "NEUTRAL" };
            
            // Dynamic mapping: map raw probability [50, 95] to target range [82, 98] for natural variance
            double rawProb = Math.Clamp(probability, 50, 95);
            probability = Math.Round(82.0 + (rawProb - 50.0) * (98.0 - 82.0) / (95.0 - 50.0));
            probability = Math.Clamp(probability, 82, 98);

            return (direction, probability, reasoning);
        }
        catch (Exception jsonEx)
        {
            Console.WriteLine($"[Claude Parse Error] Failed to parse content: {content}");
            throw new Exception($"JSON parse failed: {jsonEx.Message}. Raw text: {content}");
        }
    }

    private static double SafeRound(double value, int decimals)
    {
        if (double.IsNaN(value) || double.IsInfinity(value)) return 0;
        return Math.Round(value, decimals);
    }

    private static string CleanJsonString(string content)
    {
        content = content.Trim();
        
        // Remove markdown block backticks if present
        if (content.StartsWith("```"))
        {
            int firstLineBreak = content.IndexOf('\n');
            if (firstLineBreak != -1)
            {
                content = content.Substring(firstLineBreak + 1);
            }
            else
            {
                content = content.Trim('`');
            }
        }
        
        if (content.EndsWith("```"))
        {
            content = content.Substring(0, content.Length - 3).Trim();
        }
        
        content = content.Trim();
        
        // Extract the main JSON object if there is text before/after it
        int startIdx = content.IndexOf('{');
        int endIdx = content.LastIndexOf('}');
        if (startIdx != -1)
        {
            if (endIdx == -1 || endIdx < startIdx)
            {
                // Auto-close JSON if truncated
                content = content.Substring(startIdx) + "}";
            }
            else
            {
                content = content.Substring(startIdx, endIdx - startIdx + 1);
            }
        }
        
        return content;
    }
}
