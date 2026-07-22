using System.Text.Json.Serialization;

namespace ValutaBot.MiniApp;

public class AlertRule
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];

    [JsonPropertyName("asset")]
    public string Asset { get; set; } = "";

    [JsonPropertyName("timeframe")]
    public string Timeframe { get; set; } = "";

    [JsonPropertyName("condition")]
    public string Condition { get; set; } = "";

    [JsonPropertyName("value")]
    public double Value { get; set; }

    [JsonPropertyName("direction")]
    public string Direction { get; set; } = "";

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("chatId")]
    public long? ChatId { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}