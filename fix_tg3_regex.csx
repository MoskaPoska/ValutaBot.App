using System.IO;
using System.Text.RegularExpressions;

var lines = File.ReadAllText("MiniApp/Telegram/TelegramBotService.Commands.cs");

lines = Regex.Replace(lines, @"IsUserAllowed\(chatId\)", "await IsUserAllowed(chatId)");
lines = Regex.Replace(lines, @"IsUserAllowed\(targetId\)", "await IsUserAllowed(targetId)");

string allUsersPattern = @"lock\s*\(_lock\)\s*\{\s*if\s*\(!AllUsers\.Contains\(chatId\)\)\s*\{\s*AllUsers\.Add\(chatId\);\s*SaveAllUsers\(\);\s*\}\s*\}";
string allUsersNew = @"await BotDatabase.AddAllUserAsync(chatId);";
lines = Regex.Replace(lines, allUsersPattern, allUsersNew);

string adminCheckPattern = @"bool isAdmin;\s*lock\s*\(_lock\)\s*\{\s*isAdmin = AdminChatIds\.Contains\(chatId\);\s*\}";
string adminCheckNew = @"bool isAdmin = await BotDatabase.IsAdminAsync(chatId);";
lines = Regex.Replace(lines, adminCheckPattern, adminCheckNew);

string banPattern = @"lock\s*\(_lock\)\s*\{\s*removed = AllowedUsers\.Remove\(targetChatId\);\s*if\s*\(removed\)\s*SaveAllowedUsers\(\);\s*\}";
string banNew = @"await BotDatabase.RemoveAllowedUserAsync(targetChatId);\n                    removed = true;";
lines = Regex.Replace(lines, banPattern, banNew);

string allowUserPattern = @"lock\s*\(_lock\)\s*\{\s*if\s*\(!AllowedUsers\.Contains\(targetChatId\)\)\s*\{\s*AllowedUsers\.Add\(targetChatId\);\s*SaveAllowedUsers\(\);\s*added = true;\s*\}\s*\}";
string allowUserNew = @"await BotDatabase.AddAllowedUserAsync(targetChatId);\n                    added = true;";
lines = Regex.Replace(lines, allowUserPattern, allowUserNew);

string opPattern = @"lock\s*\(_lock\)\s*\{\s*AdminChatIds\.Add\(targetId\);\s*SaveAdmins\(\);\s*\}";
string opNew = @"await BotDatabase.AddAdminAsync(targetId);";
lines = Regex.Replace(lines, opPattern, opNew);

string deopPattern = @"lock\s*\(_lock\)\s*\{\s*AdminChatIds\.Remove\(targetId\);\s*SaveAdmins\(\);\s*\}";
string deopNew = @"await BotDatabase.RemoveAdminAsync(targetId);";
lines = Regex.Replace(lines, deopPattern, deopNew);

string dbStatsPattern = @"int totalUsers;\s*int allowedUsersCount;\s*int regsCount;\s*List<PocketRegistration> latestRegs;\s*lock\s*\(_lock\)\s*\{\s*totalUsers = AllUsers\.Count;\s*allowedUsersCount = AllowedUsers\.Count;\s*regsCount = PocketRegistrations\.Count;\s*latestRegs = PocketRegistrations\.Values\s*\.OrderByDescending\(x => x\.PocketId\)\s*\.Take\(15\)\s*\.ToList\(\);\s*\}";
string dbStatsNew = @"int totalUsers = await BotDatabase.GetTotalUsersCountAsync();
                int allowedUsersCount = await BotDatabase.GetAllowedUsersCountAsync();
                int regsCount = await BotDatabase.GetRegistrationsCountAsync();
                var latestRegs = (await BotDatabase.GetLatestRegistrationsAsync(15)).ToList();";
lines = Regex.Replace(lines, dbStatsPattern, dbStatsNew);

string bcastStatsPattern = @"int regCount,\s*allUsersCount,\s*allowedCount;\s*lock\s*\(_lock\)\s*\{\s*regCount = PocketRegistrations\.Count;\s*allUsersCount = AllUsers\.Count;\s*allowedCount = AllowedUsers\.Count;\s*\}";
string bcastStatsNew = @"int regCount = await BotDatabase.GetRegistrationsCountAsync();
                int allUsersCount = await BotDatabase.GetTotalUsersCountAsync();
                int allowedCount = await BotDatabase.GetAllowedUsersCountAsync();";
lines = Regex.Replace(lines, bcastStatsPattern, bcastStatsNew);

string approvePattern = @"lock\s*\(_lock\)\s*\{\s*if\s*\(!AllowedUsers\.Contains\(targetChatId\)\)\s*\{\s*AllowedUsers\.Add\(targetChatId\);\s*SaveAllowedUsers\(\);\s*\}\s*\}";
string approveNew = @"await BotDatabase.AddAllowedUserAsync(targetChatId);";
lines = Regex.Replace(lines, approvePattern, approveNew);

string regPattern = @"lock\s*\(_lock\)\s*\{\s*PocketRegistrations\[reg\.PocketId\] = reg;\s*SaveRegistrations\(\);\s*\}";
string regNew = @"await BotDatabase.SaveRegistrationAsync(reg);";
lines = Regex.Replace(lines, regPattern, regNew);

string fakeDepPattern = @"lock\s*\(_lock\)\s*\{\s*if\s*\(PocketRegistrations\.TryGetValue\(regId,\s*out var existingReg\)\)\s*\{\s*existingReg\.HasDeposited = true;\s*existingReg\.ChatId = chatId;\s*SaveRegistrations\(\);\s*\}\s*\}";
string fakeDepNew = @"var existingReg = await BotDatabase.GetPocketRegistrationAsync(regId);
                            if (existingReg != null)
                            {
                                existingReg.HasDeposited = true;
                                existingReg.ChatId = chatId;
                                await BotDatabase.SaveRegistrationAsync(existingReg);
                            }";
lines = Regex.Replace(lines, fakeDepPattern, fakeDepNew);

lines = lines.Replace(@"await _httpClient.PostAsync($""https://api.telegram.org/bot{token}/sendMessage"", content);", @"await _httpClient.PostAsync(new Uri($""https://api.telegram.org/bot{token}/sendMessage""), content);");

File.WriteAllText("MiniApp/Telegram/TelegramBotService.Commands.cs", lines);
