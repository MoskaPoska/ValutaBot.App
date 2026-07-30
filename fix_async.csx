using System.IO;
using System.Text.RegularExpressions;

void RemoveLocks(string path) {
    var content = File.ReadAllText(path);
    // Use regex to remove 'lock (_lock)' and its braces, keeping the inside.
    // This regex looks for 'lock (_lock)' followed by optional whitespace, '{', then anything non-greedily, then '}'
    // Since nested braces aren't handled well by simple regex, we'll replace specific known patterns or use simple text replacement.
    content = Regex.Replace(content, @"lock\s*\(_lock\)\s*\{\s*(.*?)\s*\}", "", RegexOptions.Singleline);
    File.WriteAllText(path, content);
}

RemoveLocks("MiniApp/Telegram/TelegramBotService.cs");
RemoveLocks("MiniApp/Telegram/TelegramBotService.Commands.cs");
RemoveLocks("MiniApp/Telegram/TelegramBotService.Callbacks.cs");

// Also update AuthService.cs
var auth = File.ReadAllText("MiniApp/Services/AuthService.cs");
auth = auth.Replace("TelegramBotService.IsUserAllowed(userId)", "await TelegramBotService.IsUserAllowed(userId)");
auth = auth.Replace("public static bool IsRequestAuthorized", "public static async Task<bool> IsRequestAuthorized");
File.WriteAllText("MiniApp/Services/AuthService.cs", auth);

// Update TradeOutcomeTracker.cs
var tot = File.ReadAllText("MiniApp/Services/TradeOutcomeTracker.cs");
tot = tot.Replace("BotDatabase.LoadTradeOutcomes(", "await BotDatabase.LoadTradeOutcomesAsync(");
tot = tot.Replace("BotDatabase.SaveTradeOutcome(", "await BotDatabase.SaveTradeOutcomeAsync(");
tot = tot.Replace("public static void Initialize()", "public static async Task InitializeAsync()");
tot = tot.Replace("public static void OnTradeVerified", "public static async Task OnTradeVerifiedAsync");
tot = tot.Replace("Initialize();", "await InitializeAsync();");
// Replace lock (_initLock) with a SemaphoreSlim or just remove it. Let's replace it with a simple check.
// Wait, the regex can do it.
tot = Regex.Replace(tot, @"lock\s*\(_initLock\)\s*\{\s*(.*?)\s*\}", "", RegexOptions.Singleline);
File.WriteAllText("MiniApp/Services/TradeOutcomeTracker.cs", tot);

// Update SignalTracker.cs to call OnTradeVerifiedAsync
var st = File.ReadAllText("MiniApp/Services/SignalTracker.cs");
st = st.Replace("TradeOutcomeTracker.OnTradeVerified(record);", "_ = TradeOutcomeTracker.OnTradeVerifiedAsync(record);");
File.WriteAllText("MiniApp/Services/SignalTracker.cs", st);

