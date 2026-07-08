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
        AwaitingId,
        AwaitingDeleteId
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
            InitDatabase();
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

        // Test command to reset access (Admin-only)
        if (command == "/reset" || command == "/resetaccess")
        {
            if (!isAdmin)
            {
                await SendMessage(token, chatId, "❌ У вас нет прав для выполнения этой команды.");
                return;
            }

            long targetChatId = chatId;
            var parts = cleanText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 1 && long.TryParse(parts[1], out long parsedId))
            {
                targetChatId = parsedId;
            }

            lock (_lock)
            {
                AllowedUsers.Remove(targetChatId);
                SaveAllowedUsers();
            }
            await SendMessage(token, chatId, $"🔄 <b>Доступ для пользователя {targetChatId} успешно сброшен!</b>");
            if (targetChatId != chatId)
            {
                await SendMessage(token, targetChatId, "🔄 <b>Ваш доступ был сброшен администратором.</b>");
            }
            return;
        }

        // Admin stats and registrations lookup command
        if (command == "/stats" || command == "/regs" || cleanText == "👥 Всего юзеров")
        {
            if (!isAdmin)
            {
                await SendMessage(token, chatId, "❌ У вас нет прав для выполнения этой команды.");
                return;
            }

            int totalUsers;
            int allowedUsersCount;
            int regsCount;
            List<PocketRegistration> latestRegs;

            lock (_lock)
            {
                totalUsers = AllUsers.Count;
                allowedUsersCount = AllowedUsers.Count;
                regsCount = PocketRegistrations.Count;
                latestRegs = PocketRegistrations.Values
                    .Take(15)
                    .ToList();
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("📊 <b>Статистика бота:</b>");
            sb.AppendLine($"• Всего пользователей в боте: <b>{totalUsers}</b>");
            sb.AppendLine($"• Пользователей с доступом: <b>{allowedUsersCount}</b>");
            sb.AppendLine($"• Регистраций в базе: <b>{regsCount}</b>\n");
            
            sb.AppendLine("📝 <b>Последние 15 записей в базе:</b>");
            if (latestRegs.Count == 0)
            {
                sb.AppendLine("<i>(База регистраций пуста)</i>");
            }
            else
            {
                foreach (var r in latestRegs)
                {
                    string regIcon = r.HasRegistered ? "✅" : "❌";
                    string depIcon = r.HasDeposited ? "💰" : "❌";
                    sb.AppendLine($"• Pocket ID: <code>{r.PocketId}</code> | TG Chat: <code>{r.ChatId}</code> | Рег: {regIcon} | Деп: {depIcon}");
                }
            }

            await SendMessage(token, chatId, sb.ToString());
            return;
        }

        // Admin database download command
        if (command == "/db" || command == "/getdb" || command == "/downloaddb")
        {
            if (!isAdmin)
            {
                await SendMessage(token, chatId, "❌ У вас нет прав для выполнения этой команды.");
                return;
            }

            await SendMessage(token, chatId, "⏳ Подготавливаю файлы базы данных...");
            await SendDatabaseFile(token, chatId, RegistrationsFile, "📁 База регистраций Pocket Option (registrations.json)");
            await SendDatabaseFile(token, chatId, AllowedUsersFile, "📁 Список разрешенных пользователей с доступом (allowed_users.json)");
            await SendDatabaseFile(token, chatId, AllUsersFile, "📁 Все уникальные пользователи бота (all_users.json)");
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

        if (cleanText == "🚫 Удалить доступ")
        {
            if (!isAdmin) return;
            UserStates[chatId] = UserState.AwaitingDeleteId;
            await SendMessage(token, chatId, "✍️ <b>Пожалуйста, введите Telegram Chat ID пользователя, доступ которого нужно аннулировать:</b>\n\n(Вы можете скопировать Chat ID из логов регистраций в 👥 Всего юзеров)");
            return;
        }

        if (UserStates.TryGetValue(chatId, out var state) && state == UserState.AwaitingDeleteId && isAdmin)
        {
            UserStates[chatId] = UserState.None;
            if (long.TryParse(cleanText.Trim(), out long targetChatId))
            {
                bool removed;
                lock (_lock)
                {
                    removed = AllowedUsers.Remove(targetChatId);
                    if (removed)
                    {
                        SaveAllowedUsers();
                    }
                }

                if (removed)
                {
                    await SendMessage(token, chatId, $"✅ <b>Доступ для пользователя <code>{targetChatId}</code> успешно удален из базы данных и памяти бота!</b>");
                    try
                    {
                        await SendMessage(token, targetChatId, "🔄 <b>Ваш доступ к боту был аннулирован администратором.</b>");
                    }
                    catch { /* ignore if blocked */ }
                }
                else
                {
                    await SendMessage(token, chatId, $"❓ <b>Пользователь с Chat ID <code>{targetChatId}</code> не найден в списке разрешенных.</b>");
                }
            }
            else
            {
                await SendMessage(token, chatId, "❌ <b>Неверный формат Chat ID. Действие отменено.</b>\n\nChat ID должен состоять только из цифр.");
            }
            return;
        }

        if (UserStates.TryGetValue(chatId, out state) && state == UserState.AwaitingId)
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
                            text = "✅ <b>ID сохранен. Регистрация найдена. Теперь внесите депозит на аккаунт Pocket Option (от $10) и нажмите кнопку проверки.</b>", 
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
                    await SendMessage(token, chatId, "❌ <b>Ваш ID не найден в автоматической базе регистраций.</b>\n\n" +
                                                   "Пожалуйста, убедитесь, что вы зарегистрировались по нашей ссылке.\n\n" +
                                                   "Если вы только что прошли регистрацию, брокеру может потребоваться 1-2 минуты для синхронизации данных. Пожалуйста, подождите немного и введите ваш ID еще раз.");
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
            bool isSenderAdmin;
            lock (_lock)
            {
                isSenderAdmin = AdminChatIds.Contains(chatId);
            }

            if (!isSenderAdmin)
            {
                await AnswerCallbackQuery(token, queryId, "❌ У вас нет прав для одобрения заявок.");
                return;
            }

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
                    new { text = "1️⃣ Зарегистрироваться на Pocket Option", url = $"https://po-ru4.click/cabinet/demo-quick-high-low?utm_campaign=852286&utm_source=affiliate&utm_medium=sr&a=Tlu0RchTyPcFYj&al=1775096&ac=smart-link&cid=963405&code=WELCOME50&subid={chatId}&subid1={chatId}&sub_id1={chatId}" }
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
                },
                new object[]
                {
                    new { text = "👥 Всего юзеров" },
                    new { text = "🚫 Удалить доступ" }
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

    private static string ConnectionString = "";

    private static string ConvertDatabaseUrlToConnectionString(string databaseUrl)
    {
        if (string.IsNullOrEmpty(databaseUrl)) return "";

        try
        {
            var uri = new Uri(databaseUrl);
            var userInfo = uri.UserInfo.Split(':');
            var username = userInfo[0];
            var password = userInfo.Length > 1 ? userInfo[1] : "";
            var host = uri.Host;
            var port = uri.Port > 0 ? uri.Port : 5432;
            var database = uri.AbsolutePath.TrimStart('/');

            return $"Host={host};Port={port};Username={username};Password={password};Database={database};SSL Mode=Require;Trust Server Certificate=true";
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DB] Error parsing DATABASE_URL: {ex.Message}");
            return "";
        }
    }

    private static void InitDatabase()
    {
        string? dbUrl = Environment.GetEnvironmentVariable("DATABASE_URL");
        if (string.IsNullOrEmpty(dbUrl))
        {
            Console.WriteLine("[DB] DATABASE_URL is not set. Using JSON file fallback.");
            return;
        }

        ConnectionString = ConvertDatabaseUrlToConnectionString(dbUrl);
        if (string.IsNullOrEmpty(ConnectionString)) return;

        try
        {
            using var conn = new Npgsql.NpgsqlConnection(ConnectionString);
            conn.Open();

            using (var cmd = new Npgsql.NpgsqlCommand())
            {
                cmd.Connection = conn;
                
                cmd.CommandText = @"
                    CREATE TABLE IF NOT EXISTS registrations (
                        pocket_id VARCHAR(50) PRIMARY KEY,
                        chat_id BIGINT,
                        has_registered BOOLEAN NOT NULL DEFAULT FALSE,
                        has_deposited BOOLEAN NOT NULL DEFAULT FALSE,
                        deposit_amount DOUBLE PRECISION NOT NULL DEFAULT 0
                    );";
                cmd.ExecuteNonQuery();

                cmd.CommandText = @"
                    CREATE TABLE IF NOT EXISTS allowed_users (
                        chat_id BIGINT PRIMARY KEY
                    );";
                cmd.ExecuteNonQuery();

                cmd.CommandText = @"
                    CREATE TABLE IF NOT EXISTS all_users (
                        chat_id BIGINT PRIMARY KEY
                    );";
                cmd.ExecuteNonQuery();

                cmd.CommandText = @"
                    DELETE FROM registrations 
                    WHERE pocket_id LIKE '%[%' 
                       OR pocket_id LIKE '%]%' 
                       OR pocket_id LIKE '%{%' 
                       OR pocket_id LIKE '%}%' 
                       OR LOWER(pocket_id) = 'uid';";
                cmd.ExecuteNonQuery();
            }

            Console.WriteLine("[DB] PostgreSQL database tables initialized successfully.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DB] Error initializing database: {ex.Message}");
            ConnectionString = ""; // Disable DB on error
        }
    }

    private static void LoadAllowedUsers()
    {
        if (!string.IsNullOrEmpty(ConnectionString))
        {
            try
            {
                using var conn = new Npgsql.NpgsqlConnection(ConnectionString);
                conn.Open();
                using var cmd = new Npgsql.NpgsqlCommand("SELECT chat_id FROM allowed_users", conn);
                using var reader = cmd.ExecuteReader();
                lock (_lock)
                {
                    AllowedUsers.Clear();
                    while (reader.Read())
                    {
                        AllowedUsers.Add(reader.GetInt64(0));
                    }
                }
                Console.WriteLine($"[DB] Loaded {AllowedUsers.Count} allowed users from PostgreSQL");
                return;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DB] Error loading allowed users: {ex.Message}. Falling back to file.");
            }
        }

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
        if (!string.IsNullOrEmpty(ConnectionString))
        {
            try
            {
                using var conn = new Npgsql.NpgsqlConnection(ConnectionString);
                conn.Open();
                using var trans = conn.BeginTransaction();
                
                using (var cmd = new Npgsql.NpgsqlCommand("DELETE FROM allowed_users", conn, trans))
                {
                    cmd.ExecuteNonQuery();
                }

                lock (_lock)
                {
                    foreach (var chatId in AllowedUsers)
                    {
                        using var cmd = new Npgsql.NpgsqlCommand("INSERT INTO allowed_users (chat_id) VALUES (@chatId) ON CONFLICT DO NOTHING", conn, trans);
                        cmd.Parameters.AddWithValue("chatId", chatId);
                        cmd.ExecuteNonQuery();
                    }
                }

                trans.Commit();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DB] Error saving allowed users: {ex.Message}");
            }
        }

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
        if (!string.IsNullOrEmpty(ConnectionString))
        {
            try
            {
                using var conn = new Npgsql.NpgsqlConnection(ConnectionString);
                conn.Open();
                using var cmd = new Npgsql.NpgsqlCommand("SELECT chat_id FROM all_users", conn);
                using var reader = cmd.ExecuteReader();
                lock (_lock)
                {
                    AllUsers.Clear();
                    while (reader.Read())
                    {
                        AllUsers.Add(reader.GetInt64(0));
                    }
                }
                Console.WriteLine($"[DB] Loaded {AllUsers.Count} all users from PostgreSQL");
                return;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DB] Error loading all users: {ex.Message}. Falling back to file.");
            }
        }

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
        if (!string.IsNullOrEmpty(ConnectionString))
        {
            try
            {
                using var conn = new Npgsql.NpgsqlConnection(ConnectionString);
                conn.Open();
                using var trans = conn.BeginTransaction();
                
                using (var cmd = new Npgsql.NpgsqlCommand("DELETE FROM all_users", conn, trans))
                {
                    cmd.ExecuteNonQuery();
                }

                lock (_lock)
                {
                    foreach (var chatId in AllUsers)
                    {
                        using var cmd = new Npgsql.NpgsqlCommand("INSERT INTO all_users (chat_id) VALUES (@chatId) ON CONFLICT DO NOTHING", conn, trans);
                        cmd.Parameters.AddWithValue("chatId", chatId);
                        cmd.ExecuteNonQuery();
                    }
                }

                trans.Commit();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DB] Error saving all users: {ex.Message}");
            }
        }

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
        if (!string.IsNullOrEmpty(ConnectionString))
        {
            try
            {
                using var conn = new Npgsql.NpgsqlConnection(ConnectionString);
                conn.Open();
                using var cmd = new Npgsql.NpgsqlCommand("SELECT pocket_id, chat_id, has_registered, has_deposited, deposit_amount FROM registrations", conn);
                using var reader = cmd.ExecuteReader();
                lock (_lock)
                {
                    PocketRegistrations.Clear();
                    while (reader.Read())
                    {
                        string pocketId = reader.GetString(0);
                        if (string.IsNullOrEmpty(pocketId) || 
                            pocketId.Contains("[") || pocketId.Contains("]") || 
                            pocketId.Contains("{") || pocketId.Contains("}") || 
                            pocketId.Equals("uid", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        var reg = new PocketRegistration
                        {
                            PocketId = pocketId,
                            ChatId = reader.IsDBNull(1) ? 0 : reader.GetInt64(1),
                            HasRegistered = reader.GetBoolean(2),
                            HasDeposited = reader.GetBoolean(3),
                            DepositAmount = reader.GetDouble(4)
                        };
                        PocketRegistrations[reg.PocketId] = reg;
                    }
                }
                Console.WriteLine($"[DB] Loaded {PocketRegistrations.Count} registrations from PostgreSQL");
                return;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DB] Error loading registrations: {ex.Message}. Falling back to file.");
            }
        }

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
                        if (!string.IsNullOrEmpty(reg.PocketId) && 
                            !reg.PocketId.Contains("[") && !reg.PocketId.Contains("]") && 
                            !reg.PocketId.Contains("{") && !reg.PocketId.Contains("}") && 
                            !reg.PocketId.Equals("uid", StringComparison.OrdinalIgnoreCase))
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
        if (!string.IsNullOrEmpty(ConnectionString))
        {
            try
            {
                using var conn = new Npgsql.NpgsqlConnection(ConnectionString);
                conn.Open();
                using var trans = conn.BeginTransaction();
                
                lock (_lock)
                {
                    foreach (var reg in PocketRegistrations.Values)
                    {
                        using var cmd = new Npgsql.NpgsqlCommand(@"
                            INSERT INTO registrations (pocket_id, chat_id, has_registered, has_deposited, deposit_amount)
                            VALUES (@pocketId, @chatId, @hasReg, @hasDep, @depAmt)
                            ON CONFLICT (pocket_id) 
                            DO UPDATE SET 
                                chat_id = EXCLUDED.chat_id,
                                has_registered = EXCLUDED.has_registered,
                                has_deposited = EXCLUDED.has_deposited,
                                deposit_amount = EXCLUDED.deposit_amount", conn, trans);

                        cmd.Parameters.AddWithValue("pocketId", reg.PocketId);
                        cmd.Parameters.AddWithValue("chatId", reg.ChatId);
                        cmd.Parameters.AddWithValue("hasReg", reg.HasRegistered);
                        cmd.Parameters.AddWithValue("hasDep", reg.HasDeposited);
                        cmd.Parameters.AddWithValue("depAmt", reg.DepositAmount);

                        cmd.ExecuteNonQuery();
                    }
                }

                trans.Commit();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DB] Error saving registrations to PostgreSQL: {ex.Message}");
            }
        }

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
        if (string.IsNullOrEmpty(pocketId) || 
            pocketId.Contains("[") || pocketId.Contains("]") || 
            pocketId.Contains("{") || pocketId.Contains("}") || 
            pocketId.Equals("uid", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine($"[Postback] Ignored test postback with macro placeholder: pocketId={pocketId}");
            return;
        }

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

    private static async Task SendDatabaseFile(string token, long chatId, string filePath, string caption)
    {
        if (!File.Exists(filePath))
        {
            await SendMessage(token, chatId, $"❌ Файл {Path.GetFileName(filePath)} еще не создан.");
            return;
        }

        try
        {
            using var form = new MultipartFormDataContent();
            var fileBytes = File.ReadAllBytes(filePath);
            var fileContent = new ByteArrayContent(fileBytes);
            fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
            
            form.Add(new StringContent(chatId.ToString()), "chat_id");
            form.Add(fileContent, "document", Path.GetFileName(filePath));
            form.Add(new StringContent(caption), "caption");

            await _httpClient.PostAsync($"https://api.telegram.org/bot{token}/sendDocument", form);
        }
        catch (Exception ex)
        {
            await SendMessage(token, chatId, $"❌ Ошибка отправки файла: {ex.Message}");
        }
    }
}
