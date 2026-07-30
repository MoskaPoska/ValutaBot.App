using System.IO;
using System.Text.RegularExpressions;

var lines = File.ReadAllText("MiniApp/Telegram/TelegramBotService.cs");

lines = Regex.Replace(lines, @"private static void SaveAllowedUsers\(\).*?private static async Task ResetChatMenuButton", "private static async Task ResetChatMenuButton", RegexOptions.Singleline);

File.WriteAllText("MiniApp/Telegram/TelegramBotService.cs", lines);
