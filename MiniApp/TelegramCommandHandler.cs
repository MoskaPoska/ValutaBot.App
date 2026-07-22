namespace ValutaBot.MiniApp;

public static partial class TelegramCommandHandler
{
    public static string SanitizeCommandInput(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        return text.Trim().Replace("@valutaPocket_bot", "").ToLower();
    }
}
