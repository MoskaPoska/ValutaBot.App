using System.IO;
using System.Text.RegularExpressions;

var lines = File.ReadAllText("MiniApp/Telegram/TelegramBotService.cs");

// Change IsUserAllowed to async Task<bool> and remove lock
lines = lines.Replace("public static bool IsUserAllowed(long chatId)", "public static async Task<bool> IsUserAllowed(long chatId)");
lines = lines.Replace("lock (_lock)\r\n        {\r\n            return await BotDatabase.IsAdminAsync(chatId) || await BotDatabase.IsUserAllowedAsync(chatId);\r\n        }", "return await BotDatabase.IsAdminAsync(chatId) || await BotDatabase.IsUserAllowedAsync(chatId);");
lines = lines.Replace("lock (_lock)\n        {\n            return await BotDatabase.IsAdminAsync(chatId) || await BotDatabase.IsUserAllowedAsync(chatId);\n        }", "return await BotDatabase.IsAdminAsync(chatId) || await BotDatabase.IsUserAllowedAsync(chatId);");

// Remove lock around GetAdminChatIds
lines = lines.Replace("lock (_lock)\r\n        {\r\n            adminsToNotify = await BotDatabase.GetAdminChatIdsAsync();\r\n        }", "adminsToNotify = await BotDatabase.GetAdminChatIdsAsync();");
lines = lines.Replace("lock (_lock)\n        {\n            adminsToNotify = await BotDatabase.GetAdminChatIdsAsync();\n        }", "adminsToNotify = await BotDatabase.GetAdminChatIdsAsync();");

File.WriteAllText("MiniApp/Telegram/TelegramBotService.cs", lines);

// Update calls to TelegramBotService.IsUserAllowed
var cmds = File.ReadAllText("MiniApp/Telegram/TelegramBotService.Commands.cs");
cmds = cmds.Replace("TelegramBotService.IsUserAllowed(", "await TelegramBotService.IsUserAllowed(");
File.WriteAllText("MiniApp/Telegram/TelegramBotService.Commands.cs", cmds);

