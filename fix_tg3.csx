using System.IO;
using System.Text.RegularExpressions;

var lines = File.ReadAllText("MiniApp/Telegram/TelegramBotService.Commands.cs");

lines = lines.Replace("IsUserAllowed(chatId)", "await IsUserAllowed(chatId)");
lines = lines.Replace("IsUserAllowed(targetId)", "await IsUserAllowed(targetId)");

var allUsersOld = @"            lock (_lock)
            {
                if (!AllUsers.Contains(chatId))
                {
                    AllUsers.Add(chatId);
                    SaveAllUsers();
                }
            }";
var allUsersNew = @"            await BotDatabase.AddAllUserAsync(chatId);";
lines = lines.Replace(allUsersOld, allUsersNew);
lines = lines.Replace(allUsersOld.Replace("\r\n", "\n"), allUsersNew.Replace("\r\n", "\n"));

var adminCheckOld = @"            bool isAdmin;
            lock (_lock)
            {
                isAdmin = AdminChatIds.Contains(chatId);
            }";
var adminCheckNew = @"            bool isAdmin = await BotDatabase.IsAdminAsync(chatId);";
lines = lines.Replace(adminCheckOld, adminCheckNew);
lines = lines.Replace(adminCheckOld.Replace("\r\n", "\n"), adminCheckNew.Replace("\r\n", "\n"));

var banOld = @"                    lock (_lock)
                    {
                        removed = AllowedUsers.Remove(targetChatId);
                        if (removed) SaveAllowedUsers();
                    }";
var banNew = @"                    await BotDatabase.RemoveAllowedUserAsync(targetChatId);
                    removed = true;";
lines = lines.Replace(banOld, banNew);
lines = lines.Replace(banOld.Replace("\r\n", "\n"), banNew.Replace("\r\n", "\n"));

var allowUserOld = @"                    lock (_lock)
                    {
                        if (!AllowedUsers.Contains(targetChatId))
                        {
                            AllowedUsers.Add(targetChatId);
                            SaveAllowedUsers();
                            added = true;
                        }
                    }";
var allowUserNew = @"                    await BotDatabase.AddAllowedUserAsync(targetChatId);
                    added = true;";
lines = lines.Replace(allowUserOld, allowUserNew);
lines = lines.Replace(allowUserOld.Replace("\r\n", "\n"), allowUserNew.Replace("\r\n", "\n"));

var opOld = @"                        lock (_lock)
                        {
                            AdminChatIds.Add(targetId);
                            SaveAdmins();
                        }";
var opNew = @"                        await BotDatabase.AddAdminAsync(targetId);";
lines = lines.Replace(opOld, opNew);
lines = lines.Replace(opOld.Replace("\r\n", "\n"), opNew.Replace("\r\n", "\n"));

var deopOld = @"                        lock (_lock)
                        {
                            AdminChatIds.Remove(targetId);
                            SaveAdmins();
                        }";
var deopNew = @"                        await BotDatabase.RemoveAdminAsync(targetId);";
lines = lines.Replace(deopOld, deopNew);
lines = lines.Replace(deopOld.Replace("\r\n", "\n"), deopNew.Replace("\r\n", "\n"));

var dbStatsOld = @"                int totalUsers;
                int allowedUsersCount;
                int regsCount;
                List<PocketRegistration> latestRegs;
                lock (_lock)
                {
                    totalUsers = AllUsers.Count;
                    allowedUsersCount = AllowedUsers.Count;
                    regsCount = PocketRegistrations.Count;
                    latestRegs = PocketRegistrations.Values
                        .OrderByDescending(x => x.PocketId)
                        .Take(15)
                        .ToList();
                }";
var dbStatsNew = @"                int totalUsers = await BotDatabase.GetTotalUsersCountAsync();
                int allowedUsersCount = await BotDatabase.GetAllowedUsersCountAsync();
                int regsCount = await BotDatabase.GetRegistrationsCountAsync();
                var latestRegs = await BotDatabase.GetLatestRegistrationsAsync(15);";
lines = lines.Replace(dbStatsOld, dbStatsNew);
lines = lines.Replace(dbStatsOld.Replace("\r\n", "\n"), dbStatsNew.Replace("\r\n", "\n"));

var bcastStatsOld = @"                int regCount, allUsersCount, allowedCount;
                lock (_lock)
                {
                    regCount = PocketRegistrations.Count;
                    allUsersCount = AllUsers.Count;
                    allowedCount = AllowedUsers.Count;
                }";
var bcastStatsNew = @"                int regCount = await BotDatabase.GetRegistrationsCountAsync();
                int allUsersCount = await BotDatabase.GetTotalUsersCountAsync();
                int allowedCount = await BotDatabase.GetAllowedUsersCountAsync();";
lines = lines.Replace(bcastStatsOld, bcastStatsNew);
lines = lines.Replace(bcastStatsOld.Replace("\r\n", "\n"), bcastStatsNew.Replace("\r\n", "\n"));

var approveOld = @"                    lock (_lock)
                    {
                        if (!AllowedUsers.Contains(targetChatId))
                        {
                            AllowedUsers.Add(targetChatId);
                            SaveAllowedUsers();
                        }
                    }";
var approveNew = @"                    await BotDatabase.AddAllowedUserAsync(targetChatId);";
lines = lines.Replace(approveOld, approveNew);
lines = lines.Replace(approveOld.Replace("\r\n", "\n"), approveNew.Replace("\r\n", "\n"));

var regOld = @"                            lock (_lock)
                            {
                                PocketRegistrations[reg.PocketId] = reg;
                                SaveRegistrations();
                            }";
var regNew = @"                            await BotDatabase.SaveRegistrationAsync(reg);";
lines = lines.Replace(regOld, regNew);
lines = lines.Replace(regOld.Replace("\r\n", "\n"), regNew.Replace("\r\n", "\n"));

var fakeDepOld = @"                            lock (_lock)
                            {
                                if (PocketRegistrations.TryGetValue(regId, out var existingReg))
                                {
                                    existingReg.HasDeposited = true;
                                    existingReg.ChatId = chatId;
                                    SaveRegistrations();
                                }
                            }";
var fakeDepNew = @"                            var existingReg = await BotDatabase.GetPocketRegistrationAsync(regId);
                            if (existingReg != null)
                            {
                                existingReg.HasDeposited = true;
                                existingReg.ChatId = chatId;
                                await BotDatabase.SaveRegistrationAsync(existingReg);
                            }";
lines = lines.Replace(fakeDepOld, fakeDepNew);
lines = lines.Replace(fakeDepOld.Replace("\r\n", "\n"), fakeDepNew.Replace("\r\n", "\n"));

lines = lines.Replace("await _httpClient.PostAsync($\"https://api.telegram.org/bot{token}/sendMessage\", content);", "await _httpClient.PostAsync(new Uri($\"https://api.telegram.org/bot{token}/sendMessage\"), content);");

File.WriteAllText("MiniApp/Telegram/TelegramBotService.Commands.cs", lines);
