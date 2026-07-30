using System.IO;

var lines = File.ReadAllText("MiniApp/Telegram/TelegramBotService.cs");
lines = lines.Replace("if (AdminChatIds.Contains(userId)) return true;", "if (BotDatabase.IsAdmin(userId)) return true;");
lines = lines.Replace("if (AllowedUsers.Contains(userId)) return true;", "if (BotDatabase.IsUserAllowed(userId)) return true;");
lines = lines.Replace("if (AdminChatIds.Contains(chatId)) return true;", "if (BotDatabase.IsAdmin(chatId)) return true;");
File.WriteAllText("MiniApp/Telegram/TelegramBotService.cs", lines);

var cmds = File.ReadAllText("MiniApp/Telegram/TelegramBotService.Commands.cs");
cmds = cmds.Replace("removed = BotDatabase.RemoveAllowedUser(targetChatId); removed = true;", "BotDatabase.RemoveAllowedUser(targetChatId); removed = true;");
File.WriteAllText("MiniApp/Telegram/TelegramBotService.Commands.cs", cmds);
