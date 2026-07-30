using System.IO;

var lines = File.ReadAllText("MiniApp/Telegram/TelegramBotService.Registration.cs");
var regOld = @"        lock (_lock)
        {
            PocketRegistrations[reg.PocketId] = reg;
            SaveRegistrations();
        }";
var regNew = @"        await BotDatabase.SaveRegistrationAsync(reg);";
lines = lines.Replace(regOld, regNew);
lines = lines.Replace(regOld.Replace("\r\n", "\n"), regNew.Replace("\r\n", "\n"));

var allowOld = @"        lock (_lock)
        {
            if (!AllowedUsers.Contains(chatId))
            {
                AllowedUsers.Add(chatId);
                SaveAllowedUsers();
            }
        }";
var allowNew = @"        await BotDatabase.AddAllowedUserAsync(chatId);";
lines = lines.Replace(allowOld, allowNew);
lines = lines.Replace(allowOld.Replace("\r\n", "\n"), allowNew.Replace("\r\n", "\n"));

lines = lines.Replace("public static void ProcessSuccessfulRegistration(PocketRegistration reg)", "public static async Task ProcessSuccessfulRegistration(PocketRegistration reg)");
lines = lines.Replace("public static void HandleDeposit(long chatId)", "public static async Task HandleDeposit(long chatId)");

File.WriteAllText("MiniApp/Telegram/TelegramBotService.Registration.cs", lines);
