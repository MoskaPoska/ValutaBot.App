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
}
