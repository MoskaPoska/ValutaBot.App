using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ValutaBot.MiniApp;

public class TelegramBotService : BackgroundService
{
    private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
    private static readonly string AllowedUsersFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "allowed_users.json");
    private static readonly HashSet<long> AllowedUsers = new();
    private static readonly object _lock = new();

    private enum UserState
    {
        None,
        AwaitingId
    }

    private static readonly ConcurrentDictionary<long, UserState> UserStates = new();
    private static readonly ConcurrentDictionary<long, string> UserSubmittedIds = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        string? token = TelegramNotifier.GetToken();
        if (string.IsNullOrEmpty(token))
        {
            Console.WriteLine("[TG Bot] Telegram Bot Token is not set. Bot service will not run.");
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
                            
                            string username = "";
                            if (message.TryGetProperty("from", out var fromUser))
                            {
                                username = fromUser.TryGetProperty("username", out var uProp) ? (uProp.GetString() ?? "") : "";
                            }

                            await HandleMessage(token, chatId, text, username, webAppUrl);
                        }
                        else if (update.TryGetProperty("callback_query", out var callbackQuery))
                        {
                            string queryId = callbackQuery.GetProperty("id").GetString()!;
                            long chatId = callbackQuery.GetProperty("message").GetProperty("chat").GetProperty("id").GetInt64();
                            string data = callbackQuery.GetProperty("data").GetString()!;
                            int messageId = callbackQuery.GetProperty("message").GetProperty("message_id").GetInt32();

                            string username = "";
                            if (callbackQuery.TryGetProperty("from", out var fromUser))
                            {
                                username = fromUser.TryGetProperty("username", out var uProp) ? (uProp.GetString() ?? "") : "";
                            }

                            await HandleCallback(token, queryId, chatId, data, messageId, username, webAppUrl);
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

    private static async Task HandleMessage(string token, long chatId, string text, string username, string webAppUrl)
    {
        // Owner command to configure admin
        if (text == "/setadmin")
        {
            TelegramNotifier.SetDefaultChatId(chatId);
            await SendMessage(token, chatId, $"👑 <b>Вы назначены администратором бота!</b>\n\nID чата администратора: <code>{chatId}</code>. Сюда будут приходить заявки на регистрацию.");
            return;
        }

        bool isAllowed;
        lock (_lock)
        {
            isAllowed = AllowedUsers.Contains(chatId);
        }

        if (isAllowed)
        {
            await SendWelcomeAllowed(token, chatId, webAppUrl);
            return;
        }

        if (UserStates.TryGetValue(chatId, out var state) && state == UserState.AwaitingId)
        {
            var match = Regex.Match(text, @"\d{7,10}");
            if (match.Success)
            {
                string pocketId = match.Value;
                UserSubmittedIds[chatId] = pocketId;
                UserStates[chatId] = UserState.None;

                await SendMessage(token, chatId, $"⏳ <b>Ваш ID ({pocketId}) отправлен на проверку.</b>\n\nОбычно это занимает 5-15 минут. Я пришлю вам уведомление, как только доступ будет открыт!");

                long adminChatId = TelegramNotifier.GetDefaultChatId();
                if (adminChatId > 0)
                {
                    string userDisplay = string.IsNullOrEmpty(username) ? $"ID {chatId}" : $"@{username}";
                    string adminText = $"🔔 <b>Новая заявка на доступ!</b>\n\n" +
                                       $"👤 Пользователь: {userDisplay} (Chat ID: <code>{chatId}</code>)\n" +
                                       $"🆔 Pocket Option ID: <code>{pocketId}</code>\n\n" +
                                       $"Проверьте, зарегистрирован ли этот ID по вашей ссылке в кабинете Pocket Partners.";

                    var keyboard = new
                    {
                        inline_keyboard = new object[]
                        {
                            new object[]
                            {
                                new { text = "✅ Одобрить", callback_data = $"approve_{chatId}" },
                                new { text = "❌ Отклонить", callback_data = $"decline_{chatId}" }
                            }
                        }
                    };

                    await SendMessageWithKeyboard(token, adminChatId, adminText, keyboard);
                }
                else
                {
                    Console.WriteLine($"[TG Bot] Admin Chat ID is not set. Use /setadmin in the bot first.");
                }
            }
            else
            {
                await SendMessage(token, chatId, "❌ <b>Неверный формат ID.</b>\n\nПожалуйста, введите корректный ID аккаунта Pocket Option (это число из 7-10 цифр).");
            }
            return;
        }

        await SendWelcomeGated(token, chatId);
    }

    private static async Task HandleCallback(string token, string queryId, long chatId, string data, int messageId, string username, string webAppUrl)
    {
        if (data == "check_reg")
        {
            bool isAllowed;
            lock (_lock)
            {
                isAllowed = AllowedUsers.Contains(chatId);
            }

            if (isAllowed)
            {
                await AnswerCallbackQuery(token, queryId, "Доступ уже открыт!");
                await SendWelcomeAllowed(token, chatId, webAppUrl);
                return;
            }

            UserStates[chatId] = UserState.AwaitingId;
            await AnswerCallbackQuery(token, queryId, "Введите ваш ID");
            await SendMessage(token, chatId, "✍️ <b>Пожалуйста, введите ваш ID аккаунта Pocket Option.</b>\n\n" +
                                           "Вы можете найти его в личном кабинете Pocket Option (это число из 7-10 цифр в вашем профиле).");
        }
        else if (data.StartsWith("approve_"))
        {
            long userChatId = long.Parse(data.Replace("approve_", ""));
            lock (_lock)
            {
                if (!AllowedUsers.Contains(userChatId))
                {
                    AllowedUsers.Add(userChatId);
                    SaveAllowedUsers();
                }
            }

            UserSubmittedIds.TryGetValue(userChatId, out var pocketId);
            await AnswerCallbackQuery(token, queryId, "Заявка одобрена!");

            // Notify user
            await SendMessage(token, userChatId, "🎉 <b>Поздравляем! Доступ к ИИ-анализатору открыт.</b>");
            await SendWelcomeAllowed(token, userChatId, webAppUrl);

            // Edit admin message
            string userDisplay = string.IsNullOrEmpty(username) ? $"ID {userChatId}" : $"@{username}";
            string updatedText = $"✅ <b>Заявка ОДОБРЕНА</b>\n\n" +
                                 $"👤 Пользователь: {userDisplay} (Chat ID: <code>{userChatId}</code>)\n" +
                                 $"🆔 Pocket Option ID: <code>{pocketId ?? "Не указан"}</code>";
            
            await EditMessageText(token, chatId, messageId, updatedText);
        }
        else if (data.StartsWith("decline_"))
        {
            long userChatId = long.Parse(data.Replace("decline_", ""));
            UserStates[userChatId] = UserState.None;
            UserSubmittedIds.TryGetValue(userChatId, out var pocketId);
            await AnswerCallbackQuery(token, queryId, "Заявка отклонена!");

            // Notify user
            await SendMessage(token, userChatId, "❌ <b>Доступ отклонен.</b>\n\n" +
                                               "Регистрация с указанным ID не найдена под нашей реферальной ссылкой.\n\n" +
                                               "Пожалуйста, убедитесь, что вы зарегистрировали новый аккаунт строго по нашей ссылке и ввели корректный ID.");
            await SendWelcomeGated(token, userChatId);

            // Edit admin message
            string userDisplay = string.IsNullOrEmpty(username) ? $"ID {userChatId}" : $"@{username}";
            string updatedText = $"❌ <b>Заявка ОТКЛОНЕНА</b>\n\n" +
                                 $"👤 Пользователь: {userDisplay} (Chat ID: <code>{userChatId}</code>)\n" +
                                 $"🆔 Pocket Option ID: <code>{pocketId ?? "Не указан"}</code>";

            await EditMessageText(token, chatId, messageId, updatedText);
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

    private static async Task SendMessage(string token, long chatId, string text)
    {
        try
        {
            var payload = new { chat_id = chatId, text, parse_mode = "HTML" };
            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            await _httpClient.PostAsync($"https://api.telegram.org/bot{token}/sendMessage", content);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TG Bot] sendMessage exception: {ex.Message}");
        }
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

    private static async Task EditMessageText(string token, long chatId, int messageId, string text)
    {
        try
        {
            var payload = new { chat_id = chatId, message_id = messageId, text = text, parse_mode = "HTML" };
            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            await _httpClient.PostAsync($"https://api.telegram.org/bot{token}/editMessageText", content);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TG Bot] editMessageText exception: {ex.Message}");
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
