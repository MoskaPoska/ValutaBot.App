using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ValutaBot.MiniApp;

public partial class TelegramBotService
{
    private static async Task HandleMessage(string token, long chatId, string text, string username, string webAppUrl)
    {
        try
        {
            await BotDatabase.AddAllUserAsync(chatId);

            string cleanText = text.Trim();
            string command = cleanText.Split(' ')[0].Replace("@valutaPocket_bot", "").ToLower();

            // Check Admin commands first
            bool isAdmin = await BotDatabase.IsAdminAsync(chatId);

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

                await BotDatabase.RemoveAllowedUserAsync(targetChatId);
                
                await SendMessage(token, chatId, $"🔄 <b>Доступ для пользователя {targetChatId} успешно сброшен!</b>");
                if (targetChatId != chatId)
                {
                    await SendMessage(token, targetChatId, "🔄 <b>Ваш доступ был сброшен администратором.</b>");
                }
                return;
            }

            // Admin command to grant admin rights to another user
            if (command == "/addadmin" || command == "/makeadmin" || command == "/grant")
            {
                if (!isAdmin)
                {
                    await SendMessage(token, chatId, "❌ У вас нет прав для выполнения этой команды.");
                    return;
                }

                var parts = cleanText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 1 && long.TryParse(parts[1], out long targetId))
                {
                    await BotDatabase.AddAdminAsync(targetId);
                    await BotDatabase.AddAllowedUserAsync(targetId);
                    await SendMessage(token, chatId, $"👑 <b>Пользователь {targetId} успешно назначен администратором!</b>");
                    await SendMessage(token, targetId, "👑 <b>Вам предоставили права администратора и полный доступ к боту!</b>");
                }
                else
                {
                    await SendMessage(token, chatId, "💡 <b>Использование:</b> <code>/addadmin TelegramID</code> (например: <code>/addadmin 901492845</code>)");
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

                int totalUsers = await BotDatabase.GetTotalUsersCountAsync();
                int allowedUsersCount = await BotDatabase.GetAllowedUsersCountAsync();
                int regsCount = await BotDatabase.GetRegistrationsCountAsync();
                var latestRegs = (await BotDatabase.GetLatestRegistrationsAsync(15)).ToList();

                var sb = new StringBuilder();
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

                await SendMessage(token, chatId, "⏳ База данных теперь в PostgreSQL. Для выгрузки используйте SQL-дампы.");
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
                bool isAllowedUser = await BotDatabase.IsUserAllowedAsync(chatId);

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

            if (cleanText == "👑 Добавить админа" || cleanText == "➕ Добавить админа")
            {
                if (!isAdmin) return;
                UserStates[chatId] = UserState.AwaitingAddAdminId;
                await SendMessage(token, chatId, "👑 <b>Пожалуйста, введите Telegram Chat ID пользователя, которому нужно предоставить права администратора:</b>\n\n(Вы можете скопировать Chat ID из логов регистраций в 👥 Всего юзеров)");
                return;
            }

            if (UserStates.TryGetValue(chatId, out var state) && state == UserState.AwaitingAddAdminId && isAdmin)
            {
                UserStates[chatId] = UserState.None;
                if (long.TryParse(cleanText.Trim(), out long targetChatId))
                {
                    await BotDatabase.AddAdminAsync(targetChatId);
                    await BotDatabase.AddAllowedUserAsync(targetChatId);

                    await SendMessage(token, chatId, $"👑 <b>Пользователь <code>{targetChatId}</code> успешно назначен администратором!</b>");
                    try
                    {
                        await SendMessage(token, targetChatId, "👑 <b>Вам предоставили права администратора и полный доступ к боту!</b>");
                    }
                    catch { /* ignore if blocked */ }
                }
                else
                {
                    await SendMessage(token, chatId, "❌ <b>Неверный формат Chat ID. Действие отменено.</b>\n\nChat ID должен состоять только из цифр.");
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

            if (UserStates.TryGetValue(chatId, out state) && state == UserState.AwaitingDeleteId && isAdmin)
            {
                UserStates[chatId] = UserState.None;
                if (long.TryParse(cleanText.Trim(), out long targetChatId))
                {
                    await BotDatabase.RemoveAllowedUserAsync(targetChatId);

                    await SendMessage(token, chatId, $"✅ <b>Доступ для пользователя <code>{targetChatId}</code> успешно удален из базы данных!</b>");
                    try
                    {
                        await SendMessage(token, targetChatId, "🔄 <b>Ваш доступ к боту был аннулирован администратором.</b>");
                    }
                    catch { /* ignore if blocked */ }
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

                    var reg = await BotDatabase.GetPocketRegistrationAsync(pocketId);
                    bool foundReg = reg != null && reg.HasRegistered;
                    bool hasDeposited = reg != null && reg.HasDeposited;

                    if (reg != null)
                    {
                        reg.ChatId = chatId;
                        await BotDatabase.SaveRegistrationAsync(reg);
                    }

                    if (foundReg)
                    {
                        if (hasDeposited)
                        {
                            await BotDatabase.AddAllowedUserAsync(chatId);
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
                            using var content = new StringContent(json, Encoding.UTF8, "application/json");
                            await _httpClient.PostAsync(new Uri($"https://api.telegram.org/bot{token}/sendMessage"), content);
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
            bool isUserAllowed = await BotDatabase.IsUserAllowedAsync(chatId);

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
        catch (Exception ex)
        {
            BotLogger.Error($"[TG Bot] Error handling message for chatId {chatId}", ex);
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
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            await _httpClient.PostAsync(new Uri($"https://api.telegram.org/bot{token}/sendMessage"), content);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TG Bot] SendGatedWelcome exception: {ex.Message}");
        }
    }

    private static async Task SendUserWelcome(string token, long chatId, string webAppUrl)
    {
        _ = ResetChatMenuButton(token, chatId);

        string text = "✅ <b>Доступ открыт!</b>\n\nИспользуйте кнопку <b>📊 Открыть TradeAI</b> в меню внизу чата, чтобы запустить анализатор.";

        string cacheBustedUrl = AuthService.GetSignedWebAppUrl(chatId, webAppUrl, token);

        var keyboard = new
        {
            keyboard = new object[]
            {
                new object[]
                {
                    new { text = "📊 Открыть TradeAI", web_app = new { url = cacheBustedUrl } }
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
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            await _httpClient.PostAsync(new Uri($"https://api.telegram.org/bot{token}/sendMessage"), content);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TG Bot] SendUserWelcome exception: {ex.Message}");
        }
    }

    private static async Task SendAdminWelcome(string token, long chatId, string webAppUrl)
    {
        _ = ResetChatMenuButton(token, chatId);

        string text = "👑 <b>Панель администратора TradeAI</b>\n\nИспользуйте меню внизу экрана для управления ботом.";

        string cacheBustedUrl = AuthService.GetSignedWebAppUrl(chatId, webAppUrl, token);

        var keyboard = new
        {
            keyboard = new object[]
            {
                new object[]
                {
                    new { text = "📊 Открыть TradeAI", web_app = new { url = cacheBustedUrl } }
                },
                new object[]
                {
                    new { text = "👥 Всего юзеров" },
                    new { text = "👑 Добавить админа" },
                    new { text = "🚫 Удалить доступ" }
                }
            },
            resize_keyboard = true
        };

        try
        {
            var payload = new { chat_id = chatId, text, parse_mode = "HTML", reply_markup = keyboard };
            var json = JsonSerializer.Serialize(payload);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            await _httpClient.PostAsync(new Uri($"https://api.telegram.org/bot{token}/sendMessage"), content);
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

        await BotDatabase.AddAllowedUserAsync(chatId);

        // Notify user
        await SendMessage(token, chatId, "🎉 <b>Поздравляем! Ваш аккаунт Pocket Option успешно подтвержден автоматически.</b>");
        await SendUserWelcome(token, chatId, _webAppUrl);

        // Notify admins
        List<long> adminsToNotify = await BotDatabase.GetAdminChatIdsAsync();
        
        foreach (long adminId in adminsToNotify)
        {
            await SendMessage(token, adminId, $"🔔 <b>Автоматическое открытие доступа</b>\n\nПользователь с Chat ID: <code>{chatId}</code> успешно прошел регистрацию и пополнил депозит!");
        }
    }
}
