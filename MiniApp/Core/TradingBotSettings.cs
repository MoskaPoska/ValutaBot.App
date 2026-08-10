namespace ValutaBot.MiniApp;

public class TradingBotSettings
{
    public double MlWeight { get; set; } = 0.6;
    public double MathWeight { get; set; } = 0.4;
    public int FastFailTimeoutSeconds { get; set; } = 1;
    public int HttpRetryDelayMs { get; set; } = 500;
    public int MaxHttpRetries { get; set; } = 1;
}
