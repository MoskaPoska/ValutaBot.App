using System.IO;

var lines = File.ReadAllText("MiniApp/Telegram/TelegramBotService.cs");
lines = lines.Replace("BotDatabase.Initialize();", "await BotDatabase.InitializeAsync();");
lines = lines.Replace("BotDatabase.AddAdmin(", "await BotDatabase.AddAdminAsync(");
lines = lines.Replace("AllowedUsers.Clear();\n            foreach (var id in BotDatabase.LoadAllowedUsers()) AllowedUsers.Add(id);", "");
lines = lines.Replace("AdminChatIds.Clear();\n            foreach (var id in BotDatabase.LoadAdmins()) AdminChatIds.Add(id);", "");
lines = lines.Replace("AllUsers.Clear();\n            foreach (var id in BotDatabase.LoadAllUsers()) AllUsers.Add(id);", "");
File.WriteAllText("MiniApp/Telegram/TelegramBotService.cs", lines);

var cmds = File.ReadAllText("MiniApp/Telegram/TelegramBotService.Commands.cs");
cmds = cmds.Replace("BotDatabase.AddAdmin(", "await BotDatabase.AddAdminAsync(");
cmds = cmds.Replace("AdminChatIds.Add(targetId);", "");
cmds = cmds.Replace("AllowedUsers.Add(targetId);", "await BotDatabase.AddAllowedUserAsync(targetId);");
cmds = cmds.Replace("SaveAllowedUsers();", "");
cmds = cmds.Replace("SaveRegistrations();", "");
cmds = cmds.Replace("var json = JsonSerializer.Serialize(PocketRegistrations.Values);\n                        File.WriteAllText(RegistrationsFile, json);", "");
cmds = cmds.Replace("lock (_lock)", "");
cmds = cmds.Replace("AdminChatIds.Remove(targetId);", "await BotDatabase.RemoveAdminAsync(targetId);");
cmds = cmds.Replace("BotDatabase.RemoveAdmin(", "await BotDatabase.RemoveAdminAsync(");
File.WriteAllText("MiniApp/Telegram/TelegramBotService.Commands.cs", cmds);

var stats = File.ReadAllText("MiniApp/Controllers/MiniAppController.Stats.cs");
stats = stats.Replace("if (!await AuthService.IsRequestAuthorized(context, out string? authError))", "var (isAuthorized, authError) = await AuthService.IsRequestAuthorized(context);\n        if (!isAuthorized)");
File.WriteAllText("MiniApp/Controllers/MiniAppController.Stats.cs", stats);

