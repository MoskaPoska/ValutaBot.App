using System.IO;

var lines = File.ReadAllText("MiniApp/Telegram/TelegramBotService.cs");

lines = lines.Replace("public static HashSet<long> AllowedUsers = new();", "");
lines = lines.Replace("public static HashSet<long> AdminChatIds = new();", "");
lines = lines.Replace("public static HashSet<long> AllUsers = new();", "");
lines = lines.Replace("public static ConcurrentDictionary<string, PocketRegistration> PocketRegistrations = new();", "");

var isUserAllowedOld = @"    public static bool IsUserAllowed(long chatId)
    {
        lock (_lock)
        {
            return AdminChatIds.Contains(chatId) || AllowedUsers.Contains(chatId);
        }
    }";
var isUserAllowedNew = @"    public static async Task<bool> IsUserAllowed(long chatId)
    {
        return await BotDatabase.IsAdminAsync(chatId) || await BotDatabase.IsUserAllowedAsync(chatId);
    }";
lines = lines.Replace(isUserAllowedOld, isUserAllowedNew);
lines = lines.Replace(isUserAllowedOld.Replace("\r\n", "\n"), isUserAllowedNew.Replace("\r\n", "\n"));

var adminsToNotifyOld = @"        List<long> adminsToNotify;
        lock (_lock)
        {
            adminsToNotify = AdminChatIds.ToList();
        }";
var adminsToNotifyNew = @"        List<long> adminsToNotify = await BotDatabase.GetAdminChatIdsAsync();";
lines = lines.Replace(adminsToNotifyOld, adminsToNotifyNew);
lines = lines.Replace(adminsToNotifyOld.Replace("\r\n", "\n"), adminsToNotifyNew.Replace("\r\n", "\n"));

var initBlockOld = @"            BotDatabase.Initialize();
            
            // Auto-seed admin IDs 1103551505, 901492845 and any env ADMIN_CHAT_ID / ADMIN_IDS
            BotDatabase.AddAdmin(1103551505);
            BotDatabase.AddAdmin(901492845);

            string envAdmin = Environment.GetEnvironmentVariable(""ADMIN_CHAT_ID"") ?? Environment.GetEnvironmentVariable(""ADMIN_IDS"") ?? """";
            foreach (var part in envAdmin.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (long.TryParse(part, out long parsedEnvAdmin))
                {
                    BotDatabase.AddAdmin(parsedEnvAdmin);
                }
            }

            AllowedUsers.Clear();
            foreach (var id in BotDatabase.LoadAllowedUsers()) AllowedUsers.Add(id);
            AdminChatIds.Clear();
            foreach (var id in BotDatabase.LoadAdmins()) AdminChatIds.Add(id);
            AllUsers.Clear();
            foreach (var id in BotDatabase.LoadAllUsers()) AllUsers.Add(id);
            
            PocketRegistrations.Clear();
            foreach (var reg in BotDatabase.LoadRegistrations())
            {
                PocketRegistrations[reg.PocketId] = reg;
            }";
var initBlockNew = @"            await BotDatabase.InitializeAsync();
            
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
lines = lines.Replace(initBlockOld, initBlockNew);
lines = lines.Replace(initBlockOld.Replace("\r\n", "\n"), initBlockNew.Replace("\r\n", "\n"));

var methodsOld = @"    private static void SaveAllowedUsers() { }
    private static void SaveAdmins() { }
    private static void SaveAllUsers() { }
    private static void SaveRegistrations() { }";
lines = lines.Replace(methodsOld, "");
lines = lines.Replace(methodsOld.Replace("\r\n", "\n"), "");

File.WriteAllText("MiniApp/Telegram/TelegramBotService.cs", lines);
