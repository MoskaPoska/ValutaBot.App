using System.Text.Json;

namespace ValutaBot.MiniApp;

public class TelegramBotService : BackgroundService
{
    private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
    private static readonly string AllowedUsersFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "allowed_users.json");
    private static readonly HashSet<long> AllowedUsers = new();
    private static readonly object _lock = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        string? token = Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN");
        if (string.IsNullOrEmpty(token))
        {
            Console.WriteLine("[TG Bot] TELEGRAM_BOT_TOKEN is not set. Bot service will not run.");
            return;
        }

        string webAppUrl = Environment.GetEnvironmentVariable("WEBAPP_URL") 
            ?? "https://chowder-dreamland-spotlight.ngrok-free.dev";

        Console.WriteLine($"[TG Bot] Service started. WebApp URL: {webAppUrl}");

        lock (_lock)
        {
            LoadAllowedUsers();
        }

        long offset = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                string url = $"https://api.telegram.org/bot{token}/getUpdates?offset={offset}&timeout=30";
                var response = await _httpClient.GetAsync(url, stoppingToken);
                if (!response.IsSuccessStatusCode)
                {
                    await Task.Delay(5000, stoppingToken);
                    continue;
                }

                var jsonStr = await response.Content.ReadAsStringAsync(stoppingToken);
                using var doc = JsonDocument.Parse(jsonStr);
                if (doc.RootElement.TryGetProperty("result", out var resultArr) && resultArr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var update in resultArr.EnumerateArray())
                    {
                        long updateId = update.GetProperty("update_id").GetInt64();
                        offset = updateId + 1;

                        if (update.TryGetProperty("message", out var message))
                        {
                            long chatId = message.GetProperty("chat").GetProperty("id").GetInt64();
                            string text = message.TryGetProperty("text", out var tProp) ? (tProp.GetString() ?? "") : "";

                            await HandleMessage(token, chatId, text, webAppUrl);
                        }
                        else if (update.TryGetProperty("callback_query", out var callbackQuery))
                        {
                            string queryId = callbackQuery.GetProperty("id").GetString()!;
                            long chatId = callbackQuery.GetProperty("message").GetProperty("chat").GetProperty("id").GetInt64();
                            string data = callbackQuery.GetProperty("data").GetString()!;

                            await HandleCallback(token, queryId, chatId, data, webAppUrl);
                        }
                    }
                }
            }
            catch (TaskCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TG Bot] Error in polling loop: {ex.Message}");
                await Task.Delay(5000, stoppingToken);
            }
        }
    }

    private static async Task HandleMessage(string token, long chatId, string text, string webAppUrl)
    {
        bool isAllowed;
        lock (_lock)
        {
            isAllowed = AllowedUsers.Contains(chatId);
        }

        if (isAllowed)
        {
            await SendWelcomeAllowed(token, chatId, webAppUrl);
        }
        else
        {
            await SendWelcomeGated(token, chatId);
        }
    }

    private static async Task HandleCallback(string token, string queryId, long chatId, string data, string webAppUrl)
    {
        if (data == "check_reg")
        {
            lock (_lock)
            {
                if (!AllowedUsers.Contains(chatId))
                {
                    AllowedUsers.Add(chatId);
                    SaveAllowedUsers();
                }
            }

            await AnswerCallbackQuery(token, queryId, "Доступ открыт!");
            await SendWelcomeAllowed(token, chatId, webAppUrl);
        }
    }

    private static async Task SendWelcomeGated(string token, long chatId)
    {
        string text = "🤖 <b>TradeAI — AI анализ графиков</b>\n\n" +
                      "Для доступа к анализатору нужно:\n" +
                      "1. Зарегистрироваться на Pocket Option (бонус 30% к депозиту)\n" +
                      "2. Нажать «Я зарегистрировался»\n\n" +
                      "Это занимает 1 минуту.";

        var keyboard = new
        {
            inline_keyboard = new object[]
            {
                new object[]
                {
                    new { text = "1️⃣ Зарегистрироваться на Pocket Option", url = "https://pocket-friends.co/r/d53em1oh52" }
                },
                new object[]
                {
                    new { text = "✅ Я зарегистрировался, открыть доступ", callback_data = "check_reg" }
                }
            }
        };

        await SendMessageWithKeyboard(token, chatId, text, keyboard);
    }

    private static async Task SendWelcomeAllowed(string token, long chatId, string webAppUrl)
    {
        string text = "✅ <b>Доступ открыт!</b> Нажимай «Открыть TradeAI» чтобы начать анализ.";

        var keyboard = new
        {
            inline_keyboard = new object[]
            {
                new object[]
                {
                    new { text = "📊 Открыть TradeAI", web_app = new { url = webAppUrl } }
                },
                new object[]
                {
                    new { text = "💰 Pocket Option (бонус 30%)", url = "https://pocket-friends.co/r/d53em1oh52" }
                }
            }
        };

        await SendMessageWithKeyboard(token, chatId, text, keyboard);
    }

    private static async Task SendMessageWithKeyboard(string token, long chatId, string text, object keyboard)
    {
        try
        {
            var payload = new { chat_id = chatId, text, parse_mode = "HTML", reply_markup = keyboard };
            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            var resp = await _httpClient.PostAsync($"https://api.telegram.org/bot{token}/sendMessage", content);
            if (!resp.IsSuccessStatusCode)
            {
                var err = await resp.Content.ReadAsStringAsync();
                Console.WriteLine($"[TG Bot] sendMessage error: {err}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TG Bot] sendMessage exception: {ex.Message}");
        }
    }

    private static async Task AnswerCallbackQuery(string token, string callbackQueryId, string text)
    {
        try
        {
            var payload = new { callback_query_id = callbackQueryId, text = text };
            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            await _httpClient.PostAsync($"https://api.telegram.org/bot{token}/answerCallbackQuery", content);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TG Bot] answerCallbackQuery exception: {ex.Message}");
        }
    }

    private static void LoadAllowedUsers()
    {
        try
        {
            if (File.Exists(AllowedUsersFile))
            {
                var json = File.ReadAllText(AllowedUsersFile);
                var list = JsonSerializer.Deserialize<List<long>>(json);
                if (list != null)
                {
                    AllowedUsers.Clear();
                    foreach (var id in list) AllowedUsers.Add(id);
                    Console.WriteLine($"[TG Bot] Loaded {AllowedUsers.Count} allowed users");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TG Bot] Error loading allowed users: {ex.Message}");
        }
    }

    private static void SaveAllowedUsers()
    {
        try
        {
            var json = JsonSerializer.Serialize(AllowedUsers.ToList());
            File.WriteAllText(AllowedUsersFile, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TG Bot] Error saving allowed users: {ex.Message}");
        }
    }
}
