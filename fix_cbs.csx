using System.IO;

var cbs = File.ReadAllText("MiniApp/Telegram/TelegramBotService.Callbacks.cs");
cbs = cbs.Replace("TelegramBotService.IsUserAllowed(", "await TelegramBotService.IsUserAllowed(");
File.WriteAllText("MiniApp/Telegram/TelegramBotService.Callbacks.cs", cbs);

// Also need to make sure Controllers use the async IsRequestAuthorized
var mvc = File.ReadAllText("MiniApp/Controllers/MiniAppController.cs");
mvc = mvc.Replace("AuthService.IsRequestAuthorized(", "await AuthService.IsRequestAuthorized(");
File.WriteAllText("MiniApp/Controllers/MiniAppController.cs", mvc);

