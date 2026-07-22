using System.Collections.Concurrent;

namespace ValutaBot.MiniApp;

public static class AlertService
{
    private static readonly ConcurrentDictionary<string, AlertRule> _alerts = new();
    private static long _defaultChatId;

    public static void SetDefaultChatId(long chatId)
    {
        _defaultChatId = chatId;
        TelegramNotifier.SetDefaultChatId(chatId);
    }

    public static List<AlertRule> GetAll() => _alerts.Values.ToList();

    public static AlertRule? Add(AlertRule rule)
    {
        if (rule == null) return null;
        
        if (string.IsNullOrEmpty(rule.Id))
        {
            rule.Id = Guid.NewGuid().ToString("N")[..8];
        }
        
        if (!rule.ChatId.HasValue || rule.ChatId.Value <= 0)
        {
            rule.ChatId = _defaultChatId;
        }

        _alerts[rule.Id] = rule;
        return rule;
    }

    public static bool Remove(string id)
    {
        if (string.IsNullOrEmpty(id)) return false;
        return _alerts.TryRemove(id, out _);
    }
}
