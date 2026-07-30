using System.IO;
using System.Text.RegularExpressions;

var lines = File.ReadAllText("MiniApp/Telegram/TelegramBotService.cs");

lines = Regex.Replace(lines, @"public static HashSet<long> AllowedUsers.*?new\(\);", "");
lines = Regex.Replace(lines, @"public static HashSet<long> AdminChatIds.*?new\(\);", "");
lines = Regex.Replace(lines, @"public static HashSet<long> AllUsers.*?new\(\);", "");
lines = Regex.Replace(lines, @"public static ConcurrentDictionary<string, PocketRegistration> PocketRegistrations.*?new\(\);", "");

string isUserAllowedPattern = @"public static bool IsUserAllowed\(\s*long chatId\s*\)\s*\{\s*lock\s*\(_lock\)\s*\{\s*return AdminChatIds\.Contains\(chatId\)\s*\|\|\s*AllowedUsers\.Contains\(chatId\);\s*\}\s*\}";
string isUserAllowedNew = @"public static async Task<bool> IsUserAllowed(long chatId)
    {
        return await BotDatabase.IsAdminAsync(chatId) || await BotDatabase.IsUserAllowedAsync(chatId);
    }";
lines = Regex.Replace(lines, isUserAllowedPattern, isUserAllowedNew);

string adminsToNotifyPattern = @"List<long> adminsToNotify;\s*lock\s*\(_lock\)\s*\{\s*adminsToNotify = AdminChatIds\.ToList\(\);\s*\}";
string adminsToNotifyNew = @"List<long> adminsToNotify = await BotDatabase.GetAdminChatIdsAsync();";
lines = Regex.Replace(lines, adminsToNotifyPattern, adminsToNotifyNew);

string initPattern = @"BotDatabase\.Initialize\(\);\s*// Auto-seed admin IDs.*?PocketRegistrations\[reg\.PocketId\] = reg;\s*\}";
string initNew = @"await BotDatabase.InitializeAsync();
            
            // Auto-seed admin IDs 1103551505, 901492845 and any env ADMIN_CHAT_ID / ADMIN_IDS
            await BotDatabase.AddAdminAsync(1103551505);
            await BotDatabase.AddAdminAsync(901492845);

            string envAdmin = Environment.GetEnvironmentVariable(""ADMIN_CHAT_ID"") ?? Environment.GetEnvironmentVariable(""ADMIN_IDS"") ?? """";
            foreach (var part in envAdmin.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (long.TryParse(part, out long parsedEnvAdmin))
                {
                    await BotDatabase.AddAdminAsync(parsedEnvAdmin);
                }
            }";
lines = Regex.Replace(lines, initPattern, initNew, RegexOptions.Singleline);

lines = Regex.Replace(lines, @"private static void SaveAllowedUsers\(\).*?\{.*?\}", "", RegexOptions.Singleline);
lines = Regex.Replace(lines, @"private static void SaveAdmins\(\).*?\{.*?\}", "", RegexOptions.Singleline);
lines = Regex.Replace(lines, @"private static void SaveAllUsers\(\).*?\{.*?\}", "", RegexOptions.Singleline);
lines = Regex.Replace(lines, @"private static void SaveRegistrations\(\).*?\{.*?\}", "", RegexOptions.Singleline);

File.WriteAllText("MiniApp/Telegram/TelegramBotService.cs", lines);
