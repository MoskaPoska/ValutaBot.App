using System.IO;
using System.Text.RegularExpressions;

var lines = File.ReadAllText("MiniApp/Telegram/TelegramBotService.Registration.cs");

string regPattern = @"lock\s*\(_lock\)\s*\{\s*PocketRegistrations\[reg\.PocketId\] = reg;\s*SaveRegistrations\(\);\s*\}";
string regNew = @"await BotDatabase.SaveRegistrationAsync(reg);";
lines = Regex.Replace(lines, regPattern, regNew);

string allowPattern = @"lock\s*\(_lock\)\s*\{\s*if\s*\(!AllowedUsers\.Contains\(chatId\)\)\s*\{\s*AllowedUsers\.Add\(chatId\);\s*SaveAllowedUsers\(\);\s*\}\s*\}";
string allowNew = @"await BotDatabase.AddAllowedUserAsync(chatId);";
lines = Regex.Replace(lines, allowPattern, allowNew);

lines = lines.Replace("public static void ProcessSuccessfulRegistration(PocketRegistration reg)", "public static async Task ProcessSuccessfulRegistration(PocketRegistration reg)");
lines = lines.Replace("public static void HandleDeposit(long chatId)", "public static async Task HandleDeposit(long chatId)");

File.WriteAllText("MiniApp/Telegram/TelegramBotService.Registration.cs", lines);
