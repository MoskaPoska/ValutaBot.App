using System.IO;
using System.Text.RegularExpressions;

var lines = File.ReadAllText("MiniApp/Telegram/TelegramBotService.Callbacks.cs");

lines = Regex.Replace(lines, @"bool isAllowed = IsUserAllowed\(chatId\);", "bool isAllowed = await IsUserAllowed(chatId);");

string depPattern = @"lock\s*\(_lock\)\s*\{\s*if\s*\(PocketRegistrations\.TryGetValue\(pocketId,\s*out var reg\)\)\s*\{\s*hasDeposited = reg\.HasDeposited;\s*reg\.ChatId = chatId;\s*SaveRegistrations\(\);\s*\}\s*\}";
string depNew = @"var reg = await BotDatabase.GetPocketRegistrationAsync(pocketId);
            if (reg != null)
            {
                hasDeposited = reg.HasDeposited;
                reg.ChatId = chatId;
                await BotDatabase.SaveRegistrationAsync(reg);
            }";
lines = Regex.Replace(lines, depPattern, depNew);

string allowPattern = @"lock\s*\(_lock\)\s*\{\s*if\s*\(!AllowedUsers\.Contains\(chatId\)\)\s*\{\s*AllowedUsers\.Add\(chatId\);\s*SaveAllowedUsers\(\);\s*\}\s*\}";
string allowNew = @"await BotDatabase.AddAllowedUserAsync(chatId);";
lines = Regex.Replace(lines, allowPattern, allowNew);

string adminPattern = @"lock\s*\(_lock\)\s*\{\s*isSenderAdmin = AdminChatIds\.Contains\(chatId\);\s*\}";
string adminNew = @"isSenderAdmin = await BotDatabase.IsAdminAsync(chatId);";
lines = Regex.Replace(lines, adminPattern, adminNew);

string allowUserPattern = @"lock\s*\(_lock\)\s*\{\s*if\s*\(!AllowedUsers\.Contains\(userChatId\)\)\s*\{\s*AllowedUsers\.Add\(userChatId\);\s*SaveAllowedUsers\(\);\s*\}\s*\}";
string allowUserNew = @"await BotDatabase.AddAllowedUserAsync(userChatId);";
lines = Regex.Replace(lines, allowUserPattern, allowUserNew);

lines = lines.Replace(@"await _httpClient.PostAsync($""https://api.telegram.org/bot{token}/sendMessage"", content);", @"await _httpClient.PostAsync(new Uri($""https://api.telegram.org/bot{token}/sendMessage""), content);");

File.WriteAllText("MiniApp/Telegram/TelegramBotService.Callbacks.cs", lines);
