using System.Text;
using System.Text.Json;

namespace ValutaBot.MiniApp;

public static class ClaudeSignalService
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(60) };
    private static string? _lastRawResponse;
    private static string? _lastPrimaryError;

    public static string? GetLastRawResponse() => _lastRawResponse;
    public static string? GetLastPrimaryError() => _lastPrimaryError;

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

    public static string GetGeminiApiKey()
    {
        string apiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY") ?? "";
        if (string.IsNullOrEmpty(apiKey))
        {
            try
            {
                string settingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
                if (File.Exists(settingsPath))
                {
                    var json = File.ReadAllText(settingsPath);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("GeminiApiKey", out var prop))
                    {
                        apiKey = prop.GetString() ?? "";
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Gemini] Failed to read appsettings.json: {ex.Message}");
            }
        }
        return (apiKey ?? "").Trim();
    }

    public static async Task<(string direction, double probability, string reasoning, string modelName)> AnalyzeSignal(
        string asset, double[] prices, double[] volumes,
        double rsi, double ema, double macd, double macdSignal,
        double adx, double bbZ, double volStrength, double imbalance,
        string? higherTfInfo = null,
        MiniAppController.OhlcCandle[]? ohlcCandles = null,
        List<string>? detectedPatterns = null,
        double[]? supportLevels = null,
        double[]? resistanceLevels = null,
        string timeframe = "m1",
        int candleSecondsRemaining = 60,
        int candleTotalSeconds = 60,
        double atr = 0,
        double plusDi = 0,
        double minusDi = 0)
    {

        try
        {
            string apiKey = GetOpenRouterApiKey();
            if (string.IsNullOrEmpty(apiKey))
            {
                Console.WriteLine("[Claude] No API key configured");
                return ("NEUTRAL", 50, "Ключ OpenRouter не настроен", "");
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

            // Build OHLC candles array for Claude (compact format: [O,H,L,C] per candle)
            object? candlesData = null;
            if (ohlcCandles != null && ohlcCandles.Length > 0)
            {
                int decimals = prices[^1] > 100 ? 2 : 5;
                candlesData = ohlcCandles.Select(c => new[]
                {
                    Math.Round(c.Open, decimals),
                    Math.Round(c.High, decimals),
                    Math.Round(c.Low, decimals),
                    Math.Round(c.Close, decimals)
                }).ToArray();
            }

            // Computed context for LLM
            double currentPrice = prices[^1];
            double nearestSupport = 0, nearestResistance = 0;
            if (supportLevels != null)
            {
                foreach (var s in supportLevels) // descending: first below price = nearest
                {
                    if (s < currentPrice && s > 0) { nearestSupport = s; break; }
                }
            }
            if (resistanceLevels != null)
            {
                foreach (var r in resistanceLevels) // ascending: first above price = nearest
                {
                    if (r > currentPrice && r > 0) { nearestResistance = r; break; }
                }
            }
            double distToSupport = nearestSupport > 0 ? (currentPrice - nearestSupport) / currentPrice * 100 : 0;
            double distToResistance = nearestResistance > 0 ? (nearestResistance - currentPrice) / currentPrice * 100 : 0;
            string marketRegime;
            if (adx > 25 && Math.Abs(plusDi - minusDi) > 3)
                marketRegime = plusDi > minusDi ? "BULL_TREND" : "BEAR_TREND";
            else if (adx < 20)
                marketRegime = "RANGING";
            else
                marketRegime = "WEAK_TREND";

            string indicators = JsonSerializer.Serialize(new
            {
                asset,
                current_price = SafeRound(currentPrice, 5),
                market_regime = marketRegime,
                change_percent = SafeRound(change, 2),
                mom3_percent = SafeRound(mom3, 3),
                mom5_percent = SafeRound(mom5, 3),
                rsi_14 = SafeRound(rsi, 1),
                ema_9 = SafeRound(ema, 5),
                macd = SafeRound(macd, 6),
                macd_signal = SafeRound(macdSignal, 6),
                adx_14 = SafeRound(adx, 1),
                plus_di_14 = SafeRound(plusDi, 1),
                minus_di_14 = SafeRound(minusDi, 1),
                atr_14 = SafeRound(atr, 6),
                bollinger_zscore = SafeRound(bbZ, 2),
                volume_strength = SafeRound(volStrength, 2),
                bid_ask_imbalance = SafeRound(imbalance, 3),
                volatility_per_candle = SafeRound(volatility, 6),
                local_high = SafeRound(prices.Max(), 5),
                local_low = SafeRound(prices.Min(), 5),
                nearest_support = SafeRound(nearestSupport, 5),
                nearest_resistance = SafeRound(nearestResistance, 5),
                support_distance_pct = SafeRound(distToSupport, 2),
                resistance_distance_pct = SafeRound(distToResistance, 2),
                higher_timeframe_context = higherTfInfo,
                ohlc_candles_last_30 = candlesData,
                detected_patterns = detectedPatterns ?? new List<string>(),
                support_levels = supportLevels ?? Array.Empty<double>(),
                resistance_levels = resistanceLevels ?? Array.Empty<double>(),
                candle_status = new
                {
                    seconds_remaining = candleSecondsRemaining,
                    seconds_total = candleTotalSeconds,
                    progress_percent = Math.Round((double)(candleTotalSeconds - candleSecondsRemaining) / candleTotalSeconds * 100, 0)
                }
            });

            string tfLabel = timeframe.ToLower() switch
            {
                "s3" => "3-second",
                "s5" => "5-second",
                "s10" => "10-second",
                "s15" => "15-second",
                "s30" => "30-second",
                "m1" => "1-minute",
                "m2" => "2-minute",
                "m3" => "3-minute",
                "m5" => "5-minute",
                "m15" => "15-minute",
                "m30" => "30-minute",
                "h1" => "1-hour",
                "h4" => "4-hour",
                "d1" => "1-day",
                _ => $"{timeframe}"
            };

            string claudePrompt = $"You are an elite price action trader analyzing {tfLabel} binary options. "
                + $"Market regime: {{market_regime}}. "
                + "Your job is to find HIGH-PROBABILITY setups.\n\n"
                + "Combine evidence in this order:\n"
                + "1. PATTERNS: detected_patterns are pre-computed from OHLC — trust them.\n"
                + "2. LEVELS: price is `support_distance_pct`% from nearest support, `resistance_distance_pct`% from nearest resistance.\n"
                + "3. REGIME: trending (follow +DI/-DI direction) vs ranging (use Bollinger mean reversion).\n"
                + "4. CONFIRMATION: need ≥2 indicators agreeing with the pattern.\n"
                + "5. TIMING: if candle `progress_percent` > 90% — wait for next candle.\n\n"
                + "RULES:\n"
                + "- NEUTRAL if no clear confluence. DO NOT GUESS.\n"
                + "- Reasoning MUST cite the pattern name + nearest level.\n"
                + "- 60-75% for good setups, 75-90% only for textbook.\n"
                + "- ADX<20 + no reversal = NEUTRAL (flat).\n"
                + "+DI > -DI = bullish trend; -DI > +DI = bearish.\n\n"
                + "Respond ONLY valid JSON:\n"
                + "{\"direction\": \"BUY\"|\"PUT\"|\"NEUTRAL\", \"probability\": 55-90, \"reasoning\": \"1-2 sentences in Russian — name pattern, level, and confirming indicators\"}";

            string geminiPrompt = $"Ты — снайпер бинарных опционов. Таймфрейм: {tfLabel}. Режим рынка: {{market_regime}}.\n\n"
                + "ШАГ 1 — Режим:\n"
                + "- Тренд (BULL_TREND/BEAR_TREND): торгуем по тренду, +DI/-DI указывают направление.\n"
                + "- Флэт (RANGING): ищем развороты от уровней, используем стохастик/Bollinger.\n\n"
                + "ШАГ 2 — Паттерны + Уровни:\n"
                + "- detected_patterns — точные алгоритмические паттерны. Используй их.\n"
                + "- nearest_support / nearest_resistance: цена на support_distance_pct% от поддержки, resistance_distance_pct% от сопротивления.\n"
                + "- Если цена у уровня + паттерн → сильный сигнал.\n\n"
                + "ШАГ 3 — Конфлюэнс:\n"
                + "- Паттерн должен быть подтверждён ≥2 индикаторами из: RSI, MACD, ADX, объем.\n"
                + "- Если ADX<20 и нет разворотного паттерна → NEUTRAL.\n\n"
                + "ЖЁСТКИЕ ПРАВИЛА:\n"
                + "- НЕТ чёткого сигнала → NEUTRAL. Не гадай.\n"
                + "- Вероятность: 60-75% для обычных сетов, 75-90% для эталонных.\n"
                + "- Если свеча закрывается (>90% прогресса) — не входить, ждать следующую.\n"
                + "- Обоснование на русском: назови паттерн + уровень + какие индикаторы подтверждают.\n\n"
                + "ТОЛЬКО JSON:\n"
                + "{\"direction\": \"BUY\"|\"PUT\"|\"NEUTRAL\", \"probability\": 55-90, \"reasoning\": \"1-2 предложения на русском\"}";

            string primaryModel = "anthropic/claude-sonnet-5";
            string primaryLabel = "Claude Sonnet";
            string fallbackLabel = "Gemini 2.0 Flash (Google)";

            string regimeLabel = marketRegime switch
            {
                "BULL_TREND" => "BULL_TREND (+DI > -DI)",
                "BEAR_TREND" => "BEAR_TREND (-DI > +DI)",
                "RANGING" => "RANGING (ADX < 20)",
                _ => "WEAK_TREND (ADX 20-25, no clear direction)"
            };
            string finalClaudePrompt = claudePrompt.Replace("{market_regime}", regimeLabel);
            string finalGeminiPrompt = geminiPrompt.Replace("{market_regime}", regimeLabel);

            string geminiApiKey = GetGeminiApiKey();

            try
            {
                _lastPrimaryError = null;
                var result = await SendOpenRouterRequestAsync(primaryModel, apiKey, finalClaudePrompt, asset, indicators);
                return (result.direction, result.probability, result.reasoning, primaryLabel);
            }
            catch (Exception ex)
            {
                _lastPrimaryError = ex.ToString();
                Console.WriteLine($"[AI] {primaryLabel} failed: {ex.Message}. → fallback {fallbackLabel}...");
                if (!string.IsNullOrEmpty(geminiApiKey))
                {
                    try
                    {
                        string userContent = $"Technical indicators for {asset}:\n{indicators}";
                        var fallbackResult = await SendGeminiRequestAsync(geminiApiKey, finalGeminiPrompt, userContent);
                        return (fallbackResult.direction, fallbackResult.probability, fallbackResult.reasoning, fallbackLabel);
                    }
                    catch (Exception fallbackEx)
                    {
                        Console.WriteLine($"[AI] {fallbackLabel} failed: {fallbackEx.Message}");
                        _lastRawResponse = $"ERROR: Both AI models failed. Claude: {ex.Message}. Gemini: {fallbackEx.Message}";
                        return ("NEUTRAL", 50, "ИИ временно недоступен. Запущен локальный консенсус-анализ.", "Математический анализ");
                    }
                }
                else
                {
                    Console.WriteLine($"[AI] Gemini API key not configured, no fallback available");
                    _lastRawResponse = $"ERROR: Claude failed and Gemini not configured. Claude: {ex.Message}";
                    return ("NEUTRAL", 50, "ИИ временно недоступен. Запущен локальный консенсус-анализ.", "Математический анализ");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AI] Analysis failed: {ex.Message}");
            _lastRawResponse = $"ERROR: {ex.Message}";
            return ("NEUTRAL", 50, "ИИ временно недоступен. Запущен локальный консенсус-анализ.", "Математический анализ");
        }
    }

    private static async Task<(string direction, double probability, string reasoning)> SendOpenRouterRequestAsync(
        string model, string apiKey, string systemPrompt, string asset, string indicators)
    {
        Console.WriteLine($"[Claude] Attempting request to OpenRouter with model: {model}");
        object body;
        if (model.Contains("o1-"))
        {
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
                { "max_tokens", 500 },
                { "max_completion_tokens", 500 }
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

        var response = await _http.SendAsync(request);
        string responseBody = await response.Content.ReadAsStringAsync();
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
            
            probability = Math.Clamp(probability, 50, 95);

            return (direction, probability, reasoning);
        }
        catch (Exception jsonEx)
        {
            Console.WriteLine($"[Claude Parse Error] Failed to parse content: {content}");
            throw new Exception($"JSON parse failed: {jsonEx.Message}. Raw text: {content}");
        }
    }

    private static async Task<(string direction, double probability, string reasoning)> SendGeminiRequestAsync(
        string apiKey, string systemPrompt, string userContent)
    {
        string url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.0-flash:generateContent?key={apiKey}";
        Console.WriteLine("[Gemini] Direct API request to Google Gemini 2.0 Flash");

        var body = new Dictionary<string, object>
        {
            { "system_instruction", new Dictionary<string, object>
                {
                    { "parts", new[] { new Dictionary<string, string> { { "text", systemPrompt } } } }
                }
            },
            { "contents", new[]
                {
                    new Dictionary<string, object>
                    {
                        { "role", "user" },
                        { "parts", new[] { new Dictionary<string, string> { { "text", userContent } } } }
                    }
                }
            },
            { "generationConfig", new Dictionary<string, object>
                {
                    { "temperature", 0.2 },
                    { "maxOutputTokens", 500 }
                }
            }
        };

        var json = JsonSerializer.Serialize(body);

        HttpResponseMessage response = null!;
        string responseBody = string.Empty;
        for (int retry = 0; retry <= 3; retry++)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };

            response = await _http.SendAsync(request);
            responseBody = await response.Content.ReadAsStringAsync();
            _lastRawResponse = responseBody;

            if ((int)response.StatusCode == 429 && retry < 3)
            {
                int delayMs = (int)Math.Pow(2, retry) * 1000;
                Console.WriteLine($"[Gemini] 429 rate limited, retry {retry + 1}/3 in {delayMs}ms...");
                await Task.Delay(delayMs);
                continue;
            }

            if (!response.IsSuccessStatusCode)
                throw new Exception($"Google API HTTP {(int)response.StatusCode}: {responseBody}");

            break;
        }

        using var doc = JsonDocument.Parse(responseBody);

        if (doc.RootElement.TryGetProperty("error", out var errProp))
        {
            string errMsg = errProp.TryGetProperty("message", out var msgProp) ? msgProp.GetString() ?? "" : "";
            throw new Exception($"Google API error: {errMsg}");
        }

        string content = doc.RootElement
            .GetProperty("candidates")[0]
            .GetProperty("content")
            .GetProperty("parts")[0]
            .GetProperty("text")
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
            probability = Math.Clamp(probability, 50, 95);

            return (direction, probability, reasoning);
        }
        catch (Exception jsonEx)
        {
            Console.WriteLine($"[Gemini Parse Error] Failed to parse content: {content}");
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
