using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using ValutaBot.MiniApp;
using ValutaBot.App.MiniApp;

namespace ValutaBot.App.MiniApp.Telegram.Commands
{
    /// <summary>
    /// Handles: /signal ASSET [timeframe]
    /// Example: /signal EURUSD_OTC m1
    /// Admin-only. Runs market analysis and sends result to Telegram.
    /// </summary>
    public class SignalCommand : ITelegramCommand
    {
        public bool CanHandle(long chatId, string command, string cleanText)
            => command == "/signal";

        public async Task ExecuteAsync(long chatId, string command, string cleanText,
                                       bool isAdmin, string token, string webAppUrl)
        {
            if (!isAdmin)
            {
                await TelegramBotService.SendMessage(token, chatId,
                    "⛔ Команда доступна только администраторам.");
                return;
            }

            // Parse: /signal ASSET [timeframe]
            var parts = cleanText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                await TelegramBotService.SendMessage(token, chatId,
                    "ℹ️ Использование:\n<code>/signal EURUSD_OTC</code>\n<code>/signal EURUSD_OTC m5</code>");
                return;
            }

            string asset     = parts[1];
            string timeframe = parts.Length >= 3 ? parts[2] : "m1";

            await TelegramBotService.SendMessage(token, chatId,
                $"⏳ Анализирую <b>{asset}</b> ({timeframe})…");

            try
            {
                // Use HttpFactory to call the existing /api/analysis endpoint
                var http = MiniAppController.HttpFactory!.CreateClient();
                string baseUrl = "http://localhost:5000";
                string url = $"{baseUrl}/api/analyze?asset={Uri.EscapeDataString(asset)}&timeframe={timeframe}";

                var response = await http.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                {
                    string err = await response.Content.ReadAsStringAsync();
                    await TelegramBotService.SendMessage(token, chatId,
                        $"⚠️ Ошибка API ({response.StatusCode}): {err[..Math.Min(300, err.Length)]}");
                    return;
                }

                string json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);

                // Extract key fields from the analysis result
                string text = FormatAnalysisResult(asset, timeframe, doc.RootElement);
                await TelegramBotService.SendMessage(token, chatId, text);
            }
            catch (Exception ex)
            {
                await TelegramBotService.SendMessage(token, chatId,
                    $"⚠️ Ошибка анализа: {ex.Message}");
            }
        }

        private static string FormatAnalysisResult(string asset, string timeframe, JsonElement root)
        {
            // Try to extract the most important fields
            string direction  = TryGet(root, "direction", "confluenceSignal", "signal", "recommendation");
            string confidence = TryGet(root, "confidence", "confluenceScore", "score");
            string summary    = TryGet(root, "summary", "reasoning", "description", "confluenceText");

            if (string.IsNullOrEmpty(direction) && string.IsNullOrEmpty(summary))
            {
                // Fallback: return raw JSON (truncated)
                string raw = root.ToString();
                if (raw.Length > 3500) raw = raw[..3500] + "\n…";
                return $"📊 <b>{asset}</b> | {timeframe}\n\n<pre>{raw}</pre>";
            }

            string emoji = direction?.ToUpper() switch
            {
                "UP" or "BUY" or "CALL"   => "📈",
                "DOWN" or "SELL" or "PUT" => "📉",
                _                          => "⚖️"
            };

            return $"{emoji} <b>{asset}</b> | {timeframe}\n" +
                   $"Сигнал: <b>{direction}</b>" +
                   (string.IsNullOrEmpty(confidence) ? "" : $" ({confidence}%)") +
                   (string.IsNullOrEmpty(summary) ? "" : $"\n\n{summary[..Math.Min(1000, summary.Length)]}");
        }

        private static string TryGet(JsonElement root, params string[] keys)
        {
            foreach (var key in keys)
            {
                if (root.TryGetProperty(key, out var val))
                {
                    var str = val.ValueKind == JsonValueKind.String
                        ? val.GetString() ?? ""
                        : val.ToString();
                    if (!string.IsNullOrWhiteSpace(str)) return str;
                }
            }
            return "";
        }
    }
}
