using System.Collections.Concurrent;

namespace ValutaBot.MiniApp;

public static class AlertService
{
    private static readonly ConcurrentDictionary<string, AlertRule> _alerts = new();
    private static long _defaultChatId;

    public static void SetDefaultChatId(long chatId) => _defaultChatId = chatId;

    public static List<AlertRule> GetAll() => _alerts.Values.ToList();

    public static AlertRule? Add(AlertRule rule)
    {
        rule.Id = Guid.NewGuid().ToString("N")[..8];
        rule.ChatId = rule.ChatId > 0 ? rule.ChatId : _defaultChatId;
        _alerts[rule.Id] = rule;
        return rule;
    }

    public static bool Remove(string id) => _alerts.TryRemove(id, out _);

    public static List<AlertRule> CheckAll(double price, double rsi, double volumeRatio, string asset)
    {
        var triggered = new List<AlertRule>();
        foreach (var rule in _alerts.Values)
        {
            if (!rule.Asset.Equals(asset, StringComparison.OrdinalIgnoreCase)) continue;
            if (rule.Triggered) continue;

            bool fire = rule.Indicator.ToLower() switch
            {
                "rsi" => rule.Condition == "below" ? rsi < rule.Threshold : rsi > rule.Threshold,
                "price" => rule.Condition == "below" ? price < rule.Threshold : price > rule.Threshold,
                "volume" => volumeRatio > rule.Threshold,
                _ => false
            };

            if (fire)
            {
                rule.Triggered = true;
                triggered.Add(rule);
            }
        }
        return triggered;
    }

    public static void Reset() => _alerts.Clear();
}

public class AlertRule
{
    public string Id { get; set; } = "";
    public string Asset { get; set; } = "";
    public string Indicator { get; set; } = "rsi";    // rsi, price, volume
    public string Condition { get; set; } = "below";  // below, above
    public double Threshold { get; set; } = 30;
    public long ChatId { get; set; }
    public bool Triggered { get; set; }
    public string Label => $"{Asset} {Indicator} {Condition} {Threshold}";
}
