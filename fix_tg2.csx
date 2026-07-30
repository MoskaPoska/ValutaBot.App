using System.IO;

var lines = File.ReadAllText("MiniApp/Telegram/TelegramBotService.Callbacks.cs");

lines = lines.Replace("bool isAllowed = IsUserAllowed(chatId);", "bool isAllowed = await IsUserAllowed(chatId);");

var depOld = @"            lock (_lock)
            {
                if (PocketRegistrations.TryGetValue(pocketId, out var reg))
                {
                    hasDeposited = reg.HasDeposited;
                    reg.ChatId = chatId;
                    SaveRegistrations();
                }
            }";
var depNew = @"            var reg = await BotDatabase.GetPocketRegistrationAsync(pocketId);
            if (reg != null)
            {
                hasDeposited = reg.HasDeposited;
                reg.ChatId = chatId;
                await BotDatabase.SaveRegistrationAsync(reg);
            }";
lines = lines.Replace(depOld, depNew);
lines = lines.Replace(depOld.Replace("\r\n", "\n"), depNew.Replace("\r\n", "\n"));

var allowOld = @"                lock (_lock)
                {
                    if (!AllowedUsers.Contains(chatId))
                    {
                        AllowedUsers.Add(chatId);
                        SaveAllowedUsers();
                    }
                }";
var allowNew = @"                await BotDatabase.AddAllowedUserAsync(chatId);";
lines = lines.Replace(allowOld, allowNew);
lines = lines.Replace(allowOld.Replace("\r\n", "\n"), allowNew.Replace("\r\n", "\n"));

var adminOld = @"            lock (_lock)
            {
                isSenderAdmin = AdminChatIds.Contains(chatId);
            }";
var adminNew = @"            isSenderAdmin = await BotDatabase.IsAdminAsync(chatId);";
lines = lines.Replace(adminOld, adminNew);
lines = lines.Replace(adminOld.Replace("\r\n", "\n"), adminNew.Replace("\r\n", "\n"));

var allowUserOld = @"            lock (_lock)
            {
                if (!AllowedUsers.Contains(userChatId))
                {
                    AllowedUsers.Add(userChatId);
                    SaveAllowedUsers();
                }
            }";
var allowUserNew = @"            await BotDatabase.AddAllowedUserAsync(userChatId);";
lines = lines.Replace(allowUserOld, allowUserNew);
lines = lines.Replace(allowUserOld.Replace("\r\n", "\n"), allowUserNew.Replace("\r\n", "\n"));

lines = lines.Replace("await _httpClient.PostAsync($\"https://api.telegram.org/bot{token}/sendMessage\", content);", "await _httpClient.PostAsync(new Uri($\"https://api.telegram.org/bot{token}/sendMessage\"), content);");

File.WriteAllText("MiniApp/Telegram/TelegramBotService.Callbacks.cs", lines);
