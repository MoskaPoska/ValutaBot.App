import re

with open('MiniApp/Telegram/TelegramBotService.cs', 'r', encoding='utf-8') as f:
    content = f.read()

content = re.sub(r'public static HashSet<long> AllowedUsers = new\(\);\n*', '', content)
content = re.sub(r'public static HashSet<long> AdminChatIds = new\(\);\n*', '', content)
content = re.sub(r'public static HashSet<long> AllUsers = new\(\);\n*', '', content)
content = re.sub(r'public static ConcurrentDictionary<string, PocketRegistration> PocketRegistrations = new\(\);\n*', '', content)

content = re.sub(
    r'public static bool IsUserAllowed\(long chatId\)\s*\{\s*lock \(_lock\)\s*\{\s*return AdminChatIds\.Contains\(chatId\) \|\| AllowedUsers\.Contains\(chatId\);\s*\}\s*\}',
    'public static async Task<bool> IsUserAllowed(long chatId)\n    {\n        return await BotDatabase.IsAdminAsync(chatId) || await BotDatabase.IsUserAllowedAsync(chatId);\n    }',
    content
)

content = re.sub(
    r'List<long> adminsToNotify;\s*lock \(_lock\)\s*\{\s*adminsToNotify = AdminChatIds\.ToList\(\);\s*\}',
    'List<long> adminsToNotify = await BotDatabase.GetAdminChatIdsAsync();',
    content
)

content = re.sub(
    r'BotDatabase\.Initialize\(\);\s*// Auto-seed admin IDs.*?(?=\s*var me = await)',
    '''await BotDatabase.InitializeAsync();
            
            // Auto-seed admin IDs 1103551505, 901492845 and any env ADMIN_CHAT_ID / ADMIN_IDS
            await BotDatabase.AddAdminAsync(1103551505);
            await BotDatabase.AddAdminAsync(901492845);

            string envAdmin = Environment.GetEnvironmentVariable("ADMIN_CHAT_ID") ?? Environment.GetEnvironmentVariable("ADMIN_IDS") ?? "";
            foreach (var part in envAdmin.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (long.TryParse(part, out long parsedEnvAdmin))
                {
                    await BotDatabase.AddAdminAsync(parsedEnvAdmin);
                }
            }
''',
    content, flags=re.DOTALL
)

content = re.sub(r'private static void SaveAllowedUsers\(\).*?\}\n*', '', content, flags=re.DOTALL)
content = re.sub(r'private static void SaveAdmins\(\).*?\}\n*', '', content, flags=re.DOTALL)
content = re.sub(r'private static void SaveAllUsers\(\).*?\}\n*', '', content, flags=re.DOTALL)
content = re.sub(r'private static void SaveRegistrations\(\).*?\}\n*', '', content, flags=re.DOTALL)

# Delete the old lock-based files entirely
with open('MiniApp/Telegram/TelegramBotService.cs', 'w', encoding='utf-8') as f:
    f.write(content)

print("Replaced TelegramBotService.cs")
