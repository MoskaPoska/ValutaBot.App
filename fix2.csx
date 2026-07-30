using System.IO;

var lines = File.ReadAllText("MiniApp/Telegram/TelegramBotService.cs");
lines = lines.Replace("return AdminChatIds.Contains(chatId) || AllowedUsers.Contains(chatId);", "return BotDatabase.IsAdmin(chatId) || BotDatabase.IsUserAllowed(chatId);");
lines = lines.Replace("adminsToNotify = AdminChatIds.ToList();", "adminsToNotify = BotDatabase.GetAdminChatIds();");
File.WriteAllText("MiniApp/Telegram/TelegramBotService.cs", lines);

var cmds = File.ReadAllText("MiniApp/Telegram/TelegramBotService.Commands.cs");
cmds = cmds.Replace("removed = BotDatabase.RemoveAllowedUser(targetChatId);", "BotDatabase.RemoveAllowedUser(targetChatId); removed = true;");
File.WriteAllText("MiniApp/Telegram/TelegramBotService.Commands.cs", cmds);
