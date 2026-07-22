using Telegram.Bot;
using Telegram.Bot.Types.Enums;

namespace ValutaBot.MiniApp;

/// <summary>
/// Refactored Telegram Notifier using official Telegram.Bot SDK.
/// Replaces manual HttpClient JSON string manipulation with strongly-typed TelegramBotClient API.
/// </summary>
public static class TelegramNotifier
{
    private static TelegramBotClient? _botClient;
    private static string? _botToken;
    private static long _defaultChatId;

    public static void SetDefaultChatId(long chatId) => _defaultChatId = chatId;
    public static long GetDefaultChatId() => _defaultChatId;
    public static string? GetToken() => _botToken;
    public static TelegramBotClient? GetBotClient() => _botClient;

    public static void Init(string? token)
    {
        _botToken = token;
        if (!string.IsNullOrEmpty(token))
        {
            _botClient = new TelegramBotClient(token);
            BotLogger.Info("[TG Notifier] TelegramBotClient SDK initialized successfully.");
        }
    }

    public static async Task SendMessage(long chatId, string text)
    {
        if (_botClient == null) return;

        try
        {
            await _botClient.SendTextMessageAsync(
                chatId: chatId,
                text: text,
                parseMode: ParseMode.Html
            );
        }
        catch (Exception ex)
        {
            BotLogger.Error($"[TG Notifier] SendMessage SDK exception to chatId={chatId}: {ex.Message}", ex);
        }
    }

    public static async Task SendAlert(long chatId, string title, string body, string color = "#b388ff")
    {
        string message = $"🚨 <b>{title}</b>\n\n{body}";
        await SendMessage(chatId, message);
    }
}
