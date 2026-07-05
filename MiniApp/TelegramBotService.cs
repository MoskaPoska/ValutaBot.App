using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ValutaBot.MiniApp;

public class TelegramBotService : BackgroundService
{
    private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
    private static readonly string AllowedUsersFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "allowed_users.json");
    private static readonly string AdminsFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "admins.json");
    private static readonly string AllowedAdminsFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "allowed_admin_usernames.json");
    private static readonly string AllUsersFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "all_users.json");
    private static readonly string RegistrationsFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "registrations.json");
    private static readonly ConcurrentDictionary<string, PocketRegistration> PocketRegistrations = new();

    public class PocketRegistration
    {
        public long ChatId { get; set; }
        public string PocketId { get; set; } = "";
        public bool HasRegistered { get; set; }
        public bool HasDeposited { get; set; }
        public double DepositAmount { get; set; }
    }

    private static readonly HashSet<long> AllowedUsers = new();
    private static readonly HashSet<long> AdminChatIds = new();
    private static readonly HashSet<string> AllowedAdminUsernames = new(StringComparer.OrdinalIgnoreCase) { "Vanchoys06" };
    private static readonly HashSet<long> AllUsers = new();
    private static readonly ConcurrentDictionary<long, DateTime> UserLastActivity = new();
    private static readonly object _lock = new();
    private static string _webAppUrl = "https://chowder-dreamland-spotlight.ngrok-free.dev";

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

        _webAppUrl = Environment.GetEnvironmentVariable("WEBAPP_URL") 
            ?? "https://chowder-dreamland-spotlight.ngrok-free.dev";

        Console.WriteLine($"[TG Bot] Service started. WebApp URL: {_webAppUrl}");

        lock (_lock)
        {
            LoadAllowedUsers();
            LoadAdmins();
            LoadAllowedAdminUsernames();
            LoadAllUsers();
            LoadRegistrations();
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

                            await HandleMessage(token, chatId, text, username, _webAppUrl);
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

                            await HandleCallback(token, queryId, chatId, data, messageId, username, _webAppUrl);
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
        // Track activity & unique user
        UserLastActivity[chatId] = DateTime.UtcNow;
        lock (_lock)
        {
            if (!AllUsers.Contains(chatId))
            {
                AllUsers.Add(chatId);
                SaveAllUsers();
            }
        }

        string cleanText = text.Trim();
        string command = cleanText.Split(' ')[0].Replace("@valutaPocket_bot", "").ToLower();

        // Check Admin commands first
        bool isAdmin;
        lock (_lock)
        {
            isAdmin = AdminChatIds.Contains(chatId);
        }

        // Test command to reset access
        if (command == "/reset" || command == "/resetaccess")
        {
            lock (_lock)
            {
                AllowedUsers.Remove(chatId);
                SaveAllowedUsers();
            }
            await SendMessage(token, chatId, "🔄 <b>Ваш доступ сброшен!</b> Теперь вы можете протестировать бота как новый (незарегистрированный) пользователь. Отправьте /start для начала.");
            return;
        }

        // Instruction command
        if (command == "/help" || cleanText == "❓ Инструкция")
        {
            string helpText = "📖 <b>Инструкция по использованию TradeAI:</b>\n\n" +
                               "1. Нажмите кнопку <b>📊 Открыть TradeAI</b> внизу экрана.\n" +
                               "2. Выберите интересующую валютную пару и таймфрейм.\n" +
                               "3. Бот проанализирует рынок по техническим индикаторам, объемам и выдаст точный прогноз (BUY/PUT) с процентом уверенности.";
            await SendMessage(token, chatId, helpText);
            return;
        }

        if (command == "/start")
        {
            bool isAllowedUser;
            lock (_lock)
            {
                isAllowedUser = AllowedUsers.Contains(chatId);
            }

            if (isAdmin)
            {
                await SendAdminWelcome(token, chatId, webAppUrl);
            }
            else if (isAllowedUser)
            {
                await SendUserWelcome(token, chatId, webAppUrl);
            }
            else
            {
                await SendGatedWelcome(token, chatId);
            }
            return;
        }

        if (UserStates.TryGetValue(chatId, out var state) && state == UserState.AwaitingId)
        {
            var match = Regex.Match(cleanText, @"\d{7,10}");
            if (match.Success)
            {
                string pocketId = match.Value;
                UserSubmittedIds[chatId] = pocketId;
                UserStates[chatId] = UserState.None;

                bool foundReg = false;
                bool hasDeposited = false;
                lock (_lock)
                {
                    if (PocketRegistrations.TryGetValue(pocketId, out var reg))
                    {
                        foundReg = reg.HasRegistered;
                        hasDeposited = reg.HasDeposited;
                        reg.ChatId = chatId;
                        SaveRegistrations();
                    }
                }

                if (foundReg)
                {
                    if (hasDeposited)
                    {
                        lock (_lock)
                        {
                            if (!AllowedUsers.Contains(chatId))
                            {
                                AllowedUsers.Add(chatId);
                                SaveAllowedUsers();
                            }
                        }
                        await SendMessage(token, chatId, "✅ <b>Депозит подтвержден. Доступ открыт.</b>");
                        await SendUserWelcome(token, chatId, webAppUrl);
                    }
                    else
                    {
                        var depositKeyboard = new
                        {
                            inline_keyboard = new object[]
                            {
                                new object[]
                                {
                                    new { text = "💵 Проверить депозит", callback_data = $"check_dep_{pocketId}" }
                                }
                            }
                        };
                        var payload = new 
                        { 
                            chat_id = chatId, 
                            text = "✅ <b>ID сохранен. Регистрация найдена. Теперь внеси депозит и нажми кнопку проверки.</b>", 
                            parse_mode = "HTML", 
                            reply_markup = depositKeyboard 
                        };
                        var json = JsonSerializer.Serialize(payload);
                        var content = new StringContent(json, Encoding.UTF8, "application/json");
                        await _httpClient.PostAsync($"https://api.telegram.org/bot{token}/sendMessage", content);
                    }
                }
                else
                {
                    await SendMessage(token, chatId, "⏳ <b>Ваш ID не найден в автоматической базе.</b>\n\nМы отправили запрос администраторам на ручную проверку. Пожалуйста, ожидайте уведомления!");

                    List<long> adminsToNotify;
                    lock (_lock)
                    {
                        adminsToNotify = AdminChatIds.ToList();
                    }

                    if (adminsToNotify.Count > 0)
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

                        foreach (long adminId in adminsToNotify)
                        {
                            _ = SendMessageWithKeyboard(token, adminId, adminText, keyboard);
                        }
                    }
                    else
                    {
                        Console.WriteLine($"[TG Bot] No admins registered yet.");
                    }
                }
            }
            else
            {
                await SendMessage(token, chatId, "❌ <b>Неверный формат ID.</b>\n\nПожалуйста, введите корректный ID аккаунта Pocket Option (это число из 7-10 цифр).");
            }
            return;
        }

        // Catch-all welcome screen based on role
        bool isUserAllowed;
        lock (_lock)
        {
            isUserAllowed = AllowedUsers.Contains(chatId);
        }

        if (isAdmin)
        {
            await SendAdminWelcome(token, chatId, webAppUrl);
        }
        else if (isUserAllowed)
        {
            await SendUserWelcome(token, chatId, webAppUrl);
        }
        else
        {
            await SendGatedWelcome(token, chatId);
        }
    }

    private static async Task HandleCallback(string token, string queryId, long chatId, string data, int messageId, string username, string webAppUrl)
    {
        // Track activity
        UserLastActivity[chatId] = DateTime.UtcNow;

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
                await SendUserWelcome(token, chatId, webAppUrl);
                return;
            }

            UserStates[chatId] = UserState.AwaitingId;
            await AnswerCallbackQuery(token, queryId, "Введите ваш ID");
            await SendMessage(token, chatId, "✍️ <b>Пожалуйста, введите ваш ID аккаунта Pocket Option.</b>\n\n" +
                                           "Вы можете найти его в личном кабинете Pocket Option (это число из 7-10 цифр в вашем профиле).");
        }
        else if (data.StartsWith("check_dep_"))
        {
            string pocketId = data.Replace("check_dep_", "");
            
            // Answer callback query quickly
            await AnswerCallbackQuery(token, queryId, "Проверка депозита...");
            
            // Send initial status: "Идет проверка данных на сервере брокера. Ожидай."
            await SendMessage(token, chatId, "⏳ <b>Идет проверка данных на сервере брокера. Ожидай.</b>");
            
            // Wait 3 seconds to simulate broker check
            await Task.Delay(3000);
            
            bool hasDeposited = false;
            lock (_lock)
            {
                if (PocketRegistrations.TryGetValue(pocketId, out var reg))
                {
                    hasDeposited = reg.HasDeposited;
                    reg.ChatId = chatId;
                    SaveRegistrations();
                }
            }
            
            if (hasDeposited)
            {
                lock (_lock)
                {
                    if (!AllowedUsers.Contains(chatId))
                    {
                        AllowedUsers.Add(chatId);
                        SaveAllowedUsers();
                    }
                }
                await SendMessage(token, chatId, "✅ <b>Депозит подтвержден. Доступ открыт.</b>");
                await SendUserWelcome(token, chatId, webAppUrl);
            }
            else
            {
                var retryKeyboard = new
                {
                    inline_keyboard = new object[]
                    {
                        new object[]
                        {
                            new { text = "💵 Проверить депозит снова", callback_data = $"check_dep_{pocketId}" }
                        }
                    }
                };
                var payload = new 
                { 
                    chat_id = chatId, 
                    text = "❌ <b>Депозит не обнаружен.</b>\n\nПожалуйста, убедитесь, что вы пополнили баланс на брокерском счете Pocket Option. Если вы уже внесли депозит, подождите 1-2 минуты и попробуйте снова.", 
                    parse_mode = "HTML", 
                    reply_markup = retryKeyboard 
                };
                var json = JsonSerializer.Serialize(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                await _httpClient.PostAsync($"https://api.telegram.org/bot{token}/sendMessage", content);
            }
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
            await SendUserWelcome(token, userChatId, webAppUrl);

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
            await SendGatedWelcome(token, userChatId);

            // Edit admin message
            string userDisplay = string.IsNullOrEmpty(username) ? $"ID {userChatId}" : $"@{username}";
            string updatedText = $"❌ <b>Заявка ОТКЛОНЕНА</b>\n\n" +
                                 $"👤 Пользователь: {userDisplay} (Chat ID: <code>{userChatId}</code>)\n" +
                                 $"🆔 Pocket Option ID: <code>{pocketId ?? "Не указан"}</code>";

            await EditMessageText(token, chatId, messageId, updatedText);
        }
    }

    private static async Task SendGatedWelcome(string token, long chatId)
    {
        string text = "🤖 <b>TradeAI — AI анализ графиков</b>\n\n" +
                      "Для доступа к анализатору нужно:\n" +
                      "1. Зарегистрироваться на Pocket Option\n" +
                      "2. Нажать «Я зарегистрировался»\n\n" +
                      "Это занимает 1 минуту.";

        var inlineKeyboard = new
        {
            inline_keyboard = new object[]
            {
                new object[]
                {
                    new { text = "1️⃣ Зарегистрироваться на Pocket Option", url = $"https://pocket-friends.co/r/d53em1oh52?subid={chatId}&sub1={chatId}" }
                },
                new object[]
                {
                    new { text = "✅ Я зарегистрировался, открыть доступ", callback_data = "check_reg" }
                }
            }
        };

        var replyKeyboard = new
        {
            keyboard = new object[]
            {
                new object[] { new { text = "❓ Инструкция" } }
            },
            resize_keyboard = true
        };

        try
        {
            var payload = new 
            { 
                chat_id = chatId, 
                text, 
                parse_mode = "HTML", 
                reply_markup = inlineKeyboard 
            };
            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            await _httpClient.PostAsync($"https://api.telegram.org/bot{token}/sendMessage", content);

            // Send standard reply keyboard setup as separate small introduction to keep inputs clean
            var configPayload = new 
            { 
                chat_id = chatId, 
                text = "Используйте меню внизу чата для вызова справки.", 
                reply_markup = replyKeyboard 
            };
            var configJson = JsonSerializer.Serialize(configPayload);
            var configContent = new StringContent(configJson, System.Text.Encoding.UTF8, "application/json");
            await _httpClient.PostAsync($"https://api.telegram.org/bot{token}/sendMessage", configContent);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TG Bot] SendGatedWelcome exception: {ex.Message}");
        }
    }

    private static async Task SendUserWelcome(string token, long chatId, string webAppUrl)
    {
        string text = "✅ <b>Доступ открыт!</b>\n\nИспользуйте кнопку <b>📊 Открыть TradeAI</b> в меню внизу чата, чтобы запустить анализатор.";

        var keyboard = new
        {
            keyboard = new object[]
            {
                new object[]
                {
                    new { text = "📊 Открыть TradeAI", web_app = new { url = webAppUrl } }
                },
                new object[]
                {
                    new { text = "❓ Инструкция" }
                }
            },
            resize_keyboard = true,
            is_persistent = true
        };

        try
        {
            var payload = new { chat_id = chatId, text, parse_mode = "HTML", reply_markup = keyboard };
            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            await _httpClient.PostAsync($"https://api.telegram.org/bot{token}/sendMessage", content);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TG Bot] SendUserWelcome exception: {ex.Message}");
        }
    }

    private static async Task SendAdminWelcome(string token, long chatId, string webAppUrl)
    {
        string text = "👑 <b>Панель администратора TradeAI</b>\n\nИспользуйте меню внизу экрана для управления ботом.";

        var keyboard = new
        {
            keyboard = new object[]
            {
                new object[]
                {
                    new { text = "📊 Открыть TradeAI", web_app = new { url = webAppUrl } }
                }
            },
            resize_keyboard = true
        };

        try
        {
            var payload = new { chat_id = chatId, text, parse_mode = "HTML", reply_markup = keyboard };
            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            await _httpClient.PostAsync($"https://api.telegram.org/bot{token}/sendMessage", content);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TG Bot] SendAdminWelcome exception: {ex.Message}");
        }
    }

    public static async Task AutoApproveUser(long chatId)
    {
        string? token = TelegramNotifier.GetToken();
        if (string.IsNullOrEmpty(token)) return;

        lock (_lock)
        {
            if (!AllowedUsers.Contains(chatId))
            {
                AllowedUsers.Add(chatId);
                SaveAllowedUsers();
            }
        }

        // Notify user
        await SendMessage(token, chatId, "🎉 <b>Поздравляем! Ваш аккаунт Pocket Option успешно подтвержден автоматически.</b>");
        await SendUserWelcome(token, chatId, _webAppUrl);

        // Notify admins
        List<long> adminsToNotify;
        lock (_lock)
        {
            adminsToNotify = AdminChatIds.ToList();
        }
        
        string adminText = $"⚡ <b>Авто-регистрация!</b>\n\n👤 Пользователь (Chat ID: <code>{chatId}</code>) успешно зарегистрировался по вашей ссылке и автоматически получил доступ.";
        foreach (long adminId in adminsToNotify)
        {
            _ = SendMessage(token, adminId, adminText);
        }
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

    private static void LoadAdmins()
    {
        try
        {
            AdminChatIds.Clear();

            // 1. Read from environment variable (secure admin assignment)
            string envAdmins = Environment.GetEnvironmentVariable("ADMIN_CHAT_IDS") ?? "";
            if (!string.IsNullOrEmpty(envAdmins))
            {
                var ids = envAdmins.Split(',', StringSplitOptions.RemoveEmptyEntries);
                foreach (var idStr in ids)
                {
                    if (long.TryParse(idStr.Trim(), out long id))
                    {
                        AdminChatIds.Add(id);
                    }
                }
                Console.WriteLine($"[TG Bot] Loaded {AdminChatIds.Count} admins from ADMIN_CHAT_IDS env variable");
            }

            // 2. Fallback to admins.json on disk
            if (File.Exists(AdminsFile))
            {
                var json = File.ReadAllText(AdminsFile);
                var list = JsonSerializer.Deserialize<List<long>>(json);
                if (list != null)
                {
                    foreach (var id in list)
                    {
                        AdminChatIds.Add(id);
                    }
                    Console.WriteLine($"[TG Bot] Loaded admins. Total with file fallback: {AdminChatIds.Count}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TG Bot] Error loading admins: {ex.Message}");
        }
    }

    private static void SaveAdmins()
    {
        try
        {
            var json = JsonSerializer.Serialize(AdminChatIds.ToList());
            File.WriteAllText(AdminsFile, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TG Bot] Error saving admins: {ex.Message}");
        }
    }

    private static void LoadAllowedAdminUsernames()
    {
        try
        {
            if (File.Exists(AllowedAdminsFile))
            {
                var json = File.ReadAllText(AllowedAdminsFile);
                var list = JsonSerializer.Deserialize<List<string>>(json);
                if (list != null)
                {
                    AllowedAdminUsernames.Clear();
                    foreach (var username in list) AllowedAdminUsernames.Add(username);
                    Console.WriteLine($"[TG Bot] Loaded {AllowedAdminUsernames.Count} allowed admin usernames");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TG Bot] Error loading allowed admin usernames: {ex.Message}");
        }
    }

    private static void SaveAllowedAdminUsernames()
    {
        try
        {
            var json = JsonSerializer.Serialize(AllowedAdminUsernames.ToList());
            File.WriteAllText(AllowedAdminsFile, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TG Bot] Error saving allowed admin usernames: {ex.Message}");
        }
    }

    private static void LoadAllUsers()
    {
        try
        {
            if (File.Exists(AllUsersFile))
            {
                var json = File.ReadAllText(AllUsersFile);
                var list = JsonSerializer.Deserialize<List<long>>(json);
                if (list != null)
                {
                    AllUsers.Clear();
                    foreach (var id in list) AllUsers.Add(id);
                    Console.WriteLine($"[TG Bot] Loaded {AllUsers.Count} all users");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TG Bot] Error loading all users: {ex.Message}");
        }
    }

    private static void SaveAllUsers()
    {
        try
        {
            var json = JsonSerializer.Serialize(AllUsers.ToList());
            File.WriteAllText(AllUsersFile, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TG Bot] Error saving all users: {ex.Message}");
        }
    }

    private static void LoadRegistrations()
    {
        try
        {
            if (File.Exists(RegistrationsFile))
            {
                var json = File.ReadAllText(RegistrationsFile);
                var list = JsonSerializer.Deserialize<List<PocketRegistration>>(json);
                if (list != null)
                {
                    PocketRegistrations.Clear();
                    foreach (var reg in list)
                    {
                        if (!string.IsNullOrEmpty(reg.PocketId))
                        {
                            PocketRegistrations[reg.PocketId] = reg;
                        }
                    }
                    Console.WriteLine($"[TG Bot] Loaded {PocketRegistrations.Count} pocket registrations");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TG Bot] Error loading registrations: {ex.Message}");
        }
    }

    private static void SaveRegistrations()
    {
        try
        {
            var json = JsonSerializer.Serialize(PocketRegistrations.Values.ToList());
            File.WriteAllText(RegistrationsFile, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TG Bot] Error saving registrations: {ex.Message}");
        }
    }

    public static async Task ProcessPostback(long chatId, string pocketId, string status, double deposit)
    {
        lock (_lock)
        {
            var reg = PocketRegistrations.GetOrAdd(pocketId, pid => new PocketRegistration { PocketId = pid });
            if (chatId > 0) reg.ChatId = chatId;
            
            if (status == "register" || status == "reg" || status == "lead" || status == "registration")
            {
                reg.HasRegistered = true;
            }
            
            if (status == "deposit" || deposit > 0)
            {
                reg.HasDeposited = true;
                reg.DepositAmount += deposit;
            }
            
            SaveRegistrations();
        }

        if ((status == "deposit" || deposit > 0) && chatId > 0)
        {
            lock (_lock)
            {
                if (!AllowedUsers.Contains(chatId))
                {
                    AllowedUsers.Add(chatId);
                    SaveAllowedUsers();
                }
            }
            string? token = TelegramNotifier.GetToken();
            if (!string.IsNullOrEmpty(token))
            {
                await SendMessage(token, chatId, "🎉 <b>Депозит подтвержден. Доступ открыт!</b>");
                await SendUserWelcome(token, chatId, _webAppUrl);
            }
        }
    }
}
