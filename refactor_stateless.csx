using System.IO;
using System.Text.RegularExpressions;

string Normalize(string text) => text.Replace("\r\n", "\n");

void ProcessFile(string path, Func<string, string> transformer)
{
    var content = Normalize(File.ReadAllText(path));
    var newContent = transformer(content);
    File.WriteAllText(path, newContent);
}

// 1. TelegramBotService.cs
ProcessFile("MiniApp/Telegram/TelegramBotService.cs", content => {
    content = Regex.Replace(content, @"public static HashSet<long> AllowedUsers.*?\n", "");
    content = Regex.Replace(content, @"public static HashSet<long> AdminChatIds.*?\n", "");
    content = Regex.Replace(content, @"public static HashSet<long> AllUsers.*?\n", "");
    content = Regex.Replace(content, @"public static ConcurrentDictionary<string, PocketRegistration> PocketRegistrations.*?\n", "");
    
    string isUserAllowed = @"public static bool IsUserAllowed(long chatId)
    {
        lock (_lock)
        {
            return AdminChatIds.Contains(chatId) || AllowedUsers.Contains(chatId);
        }
    }";
    string isUserAllowedNew = @"public static async Task<bool> IsUserAllowed(long chatId)
    {
        return await BotDatabase.IsAdminAsync(chatId) || await BotDatabase.IsUserAllowedAsync(chatId);
    }";
    content = content.Replace(Normalize(isUserAllowed), isUserAllowedNew);

    string adminsToNotify = @"List<long> adminsToNotify;
        lock (_lock)
        {
            adminsToNotify = AdminChatIds.ToList();
        }";
    string adminsToNotifyNew = @"List<long> adminsToNotify = await BotDatabase.GetAdminChatIdsAsync();";
    content = content.Replace(Normalize(adminsToNotify), adminsToNotifyNew);

    string initBlock = @"public static void Initialize()
    {
        lock (_lock)
        {
            BotDatabase.Initialize();
            
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
            }

            var me = BotClient.GetMeAsync().Result;
            BotLogger.Info($""[TG Bot] Initialized Bot @{me.Username}"");
        }
    }";
    string initBlockNew = @"public static async Task InitializeAsync()
    {
        await BotDatabase.InitializeAsync();
        
        // Auto-seed admin IDs
        await BotDatabase.AddAdminAsync(1103551505);
        await BotDatabase.AddAdminAsync(901492845);

        string envAdmin = Environment.GetEnvironmentVariable(""ADMIN_CHAT_ID"") ?? Environment.GetEnvironmentVariable(""ADMIN_IDS"") ?? """";
        foreach (var part in envAdmin.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (long.TryParse(part, out long parsedEnvAdmin))
            {
                await BotDatabase.AddAdminAsync(parsedEnvAdmin);
            }
        }

        var me = await BotClient.GetMeAsync();
        BotLogger.Info($""[TG Bot] Initialized Bot @{me.Username}"");
    }";
    content = content.Replace(Normalize(initBlock), initBlockNew);
    
    // Remove Save methods completely
    content = Regex.Replace(content, @"(?s)private static void SaveAllowedUsers\(\).*?private static async Task ResetChatMenuButton", "private static async Task ResetChatMenuButton");
    
    return content;
});

// 2. TelegramBotService.Callbacks.cs
ProcessFile("MiniApp/Telegram/TelegramBotService.Callbacks.cs", content => {
    content = content.Replace("bool isAllowed = IsUserAllowed(chatId);", "bool isAllowed = await IsUserAllowed(chatId);");
    content = Regex.Replace(content, @"(?s)lock \(_lock\)\s*\{\s*if \(PocketRegistrations\.TryGetValue\(pocketId, out var reg\)\)\s*\{\s*hasDeposited = reg\.HasDeposited;\s*reg\.ChatId = chatId;\s*SaveRegistrations\(\);\s*\}\s*\}", @"var reg = await BotDatabase.GetPocketRegistrationAsync(pocketId);
            if (reg != null)
            {
                hasDeposited = reg.HasDeposited;
                reg.ChatId = chatId;
                await BotDatabase.SaveRegistrationAsync(reg);
            }");
    content = Regex.Replace(content, @"(?s)lock \(_lock\)\s*\{\s*if \(!AllowedUsers\.Contains\(chatId\)\)\s*\{\s*AllowedUsers\.Add\(chatId\);\s*SaveAllowedUsers\(\);\s*\}\s*\}", "await BotDatabase.AddAllowedUserAsync(chatId);");
    content = Regex.Replace(content, @"(?s)lock \(_lock\)\s*\{\s*isSenderAdmin = AdminChatIds\.Contains\(chatId\);\s*\}", "isSenderAdmin = await BotDatabase.IsAdminAsync(chatId);");
    content = Regex.Replace(content, @"(?s)lock \(_lock\)\s*\{\s*if \(!AllowedUsers\.Contains\(userChatId\)\)\s*\{\s*AllowedUsers\.Add\(userChatId\);\s*SaveAllowedUsers\(\);\s*\}\s*\}", "await BotDatabase.AddAllowedUserAsync(userChatId);");
    content = content.Replace("await _httpClient.PostAsync($\"https://api.telegram.org/bot{token}/sendMessage\", content);", "await _httpClient.PostAsync(new Uri($\"https://api.telegram.org/bot{token}/sendMessage\"), content);");
    return content;
});

// 3. TelegramBotService.Commands.cs
ProcessFile("MiniApp/Telegram/TelegramBotService.Commands.cs", content => {
    content = content.Replace("IsUserAllowed(chatId)", "await IsUserAllowed(chatId)");
    content = content.Replace("IsUserAllowed(targetId)", "await IsUserAllowed(targetId)");
    
    content = Regex.Replace(content, @"(?s)lock \(_lock\)\s*\{\s*if \(!AllUsers\.Contains\(chatId\)\)\s*\{\s*AllUsers\.Add\(chatId\);\s*SaveAllUsers\(\);\s*\}\s*\}", "await BotDatabase.AddAllUserAsync(chatId);");
    content = Regex.Replace(content, @"(?s)bool isAdmin;\s*lock \(_lock\)\s*\{\s*isAdmin = AdminChatIds\.Contains\(chatId\);\s*\}", "bool isAdmin = await BotDatabase.IsAdminAsync(chatId);");
    content = Regex.Replace(content, @"(?s)lock \(_lock\)\s*\{\s*removed = AllowedUsers\.Remove\(targetChatId\);\s*if \(removed\) SaveAllowedUsers\(\);\s*\}", "await BotDatabase.RemoveAllowedUserAsync(targetChatId);\n                    removed = true;");
    content = Regex.Replace(content, @"(?s)lock \(_lock\)\s*\{\s*if \(!AllowedUsers\.Contains\(targetChatId\)\)\s*\{\s*AllowedUsers\.Add\(targetChatId\);\s*SaveAllowedUsers\(\);\s*added = true;\s*\}\s*\}", "await BotDatabase.AddAllowedUserAsync(targetChatId);\n                    added = true;");
    
    content = Regex.Replace(content, @"(?s)lock \(_lock\)\s*\{\s*BotDatabase\.AddAdmin\(targetId\);\s*AdminChatIds\.Add\(targetId\);\s*AllowedUsers\.Add\(targetId\);\s*\}", "await BotDatabase.AddAdminAsync(targetId);\n                    await BotDatabase.AddAllowedUserAsync(targetId);");
    
    content = Regex.Replace(content, @"(?s)lock \(_lock\)\s*\{\s*AdminChatIds\.Remove\(targetId\);\s*SaveAdmins\(\);\s*\}", "await BotDatabase.RemoveAdminAsync(targetId);");
    
    content = Regex.Replace(content, @"(?s)int totalUsers;\s*int allowedUsersCount;\s*int regsCount;\s*List<PocketRegistration> latestRegs;\s*lock \(_lock\)\s*\{\s*totalUsers = AllUsers\.Count;\s*allowedUsersCount = AllowedUsers\.Count;\s*regsCount = PocketRegistrations\.Count;\s*latestRegs = PocketRegistrations\.Values\s*\.OrderByDescending\(x => x\.PocketId\)\s*\.Take\(15\)\s*\.ToList\(\);\s*\}", 
        @"int totalUsers = await BotDatabase.GetTotalUsersCountAsync();
                int allowedUsersCount = await BotDatabase.GetAllowedUsersCountAsync();
                int regsCount = await BotDatabase.GetRegistrationsCountAsync();
                var latestRegs = (await BotDatabase.GetLatestRegistrationsAsync(15)).ToList();");
                
    content = Regex.Replace(content, @"(?s)int regCount, allUsersCount, allowedCount;\s*lock \(_lock\)\s*\{\s*regCount = PocketRegistrations\.Count;\s*allUsersCount = AllUsers\.Count;\s*allowedCount = AllowedUsers\.Count;\s*\}", 
        @"int regCount = await BotDatabase.GetRegistrationsCountAsync();
                int allUsersCount = await BotDatabase.GetTotalUsersCountAsync();
                int allowedCount = await BotDatabase.GetAllowedUsersCountAsync();");

    content = Regex.Replace(content, @"(?s)lock \(_lock\)\s*\{\s*PocketRegistrations\[reg\.PocketId\] = reg;\s*SaveRegistrations\(\);\s*\}", "await BotDatabase.SaveRegistrationAsync(reg);");
    content = Regex.Replace(content, @"(?s)var json = JsonSerializer\.Serialize\(PocketRegistrations\.Values\);\s*File\.WriteAllText\(RegistrationsFile, json\);", "");
    
    content = Regex.Replace(content, @"(?s)lock \(_lock\)\s*\{\s*if \(PocketRegistrations\.TryGetValue\(regId, out var existingReg\)\)\s*\{\s*existingReg\.HasDeposited = true;\s*existingReg\.ChatId = chatId;\s*SaveRegistrations\(\);\s*\}\s*\}", 
        @"var existingReg = await BotDatabase.GetPocketRegistrationAsync(regId);
                            if (existingReg != null)
                            {
                                existingReg.HasDeposited = true;
                                existingReg.ChatId = chatId;
                                await BotDatabase.SaveRegistrationAsync(existingReg);
                            }");

    content = content.Replace("await _httpClient.PostAsync($\"https://api.telegram.org/bot{token}/sendMessage\", content);", "await _httpClient.PostAsync(new Uri($\"https://api.telegram.org/bot{token}/sendMessage\"), content);");
    
    return content;
});

// 4. TelegramBotService.Registration.cs
ProcessFile("MiniApp/Telegram/TelegramBotService.Registration.cs", content => {
    content = content.Replace("public static void ProcessSuccessfulRegistration(PocketRegistration reg)", "public static async Task ProcessSuccessfulRegistration(PocketRegistration reg)");
    content = content.Replace("public static void HandleDeposit(long chatId)", "public static async Task HandleDeposit(long chatId)");
    content = Regex.Replace(content, @"(?s)lock \(_lock\)\s*\{\s*PocketRegistrations\[reg\.PocketId\] = reg;\s*SaveRegistrations\(\);\s*\}", "await BotDatabase.SaveRegistrationAsync(reg);");
    content = Regex.Replace(content, @"(?s)lock \(_lock\)\s*\{\s*if \(!AllowedUsers\.Contains\(chatId\)\)\s*\{\s*AllowedUsers\.Add\(chatId\);\s*SaveAllowedUsers\(\);\s*\}\s*\}", "await BotDatabase.AddAllowedUserAsync(chatId);");
    return content;
});

// 5. AuthService.cs
ProcessFile("MiniApp/Services/AuthService.cs", content => {
    content = content.Replace("public static async Task<bool> IsRequestAuthorized(HttpContext context, out string? errorMessage)", "public static async Task<(bool isAuthorized, string? errorMessage)> IsRequestAuthorized(HttpContext context)");
    content = content.Replace("errorMessage = null;", "string? errorMessage = null;");
    content = content.Replace("return true;", "return (true, null);");
    content = content.Replace("return false;", "return (false, errorMessage);");
    return content;
});

// 6. MiniAppController.Stats.cs
ProcessFile("MiniApp/Controllers/MiniAppController.Stats.cs", content => {
    content = content.Replace("if (!await AuthService.IsRequestAuthorized(context, out string? authError))", "var (isAuthorized, authError) = await AuthService.IsRequestAuthorized(context);\n        if (!isAuthorized)");
    return content;
});
