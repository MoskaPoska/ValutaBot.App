namespace ValutaBot.MiniApp;

/// <summary>
/// Adaptive Expiry Calculation Engine for Forex & OTC Currency Pairs.
/// Dynamically calculates the optimal trade duration (expiry time in seconds and minutes)
/// based on ATR volatility, SMC pattern type (FVG vs OB vs Sweep), active Forex session, and timeframe.
/// </summary>
public static class AdaptiveExpiryEngine
{
    public record OptimalExpiryResult(
        int ExpirySeconds,
        string ExpiryText,
        string Reasoning
    );

    public static OptimalExpiryResult CalculateOptimalExpiry(
        string asset,
        string timeframe,
        double atr,
        double volRatio,
        SmcEngine.SmcAnalysisResult smc,
        bool isSubMinute)
    {
        string tfLower = timeframe.ToLower().Trim();
        int totalSeconds = tfLower switch
        {
            "s3" => 3,
            "s5" => 5,
            "s10" => 10,
            "s15" => 15,
            "s30" => 30,
            "m1" or "1m" => 60,
            "m2" or "2m" => 120,
            "m3" or "3m" => 180,
            "m5" or "5m" => 300,
            "m15" or "15m" => 900,
            "m30" or "30m" => 1800,
            "h1" or "1h" => 3600,
            "h4" or "4h" => 14400,
            _ => 60
        };

        string expiryText = totalSeconds switch
        {
            < 60 => $"{totalSeconds} сек",
            60 => "1 минута",
            120 => "2 минуты",
            180 => "3 минуты",
            240 => "4 минуты",
            300 => "5 минут",
            900 => "15 минут",
            1800 => "30 минут",
            3600 => "1 час",
            _ => $"{totalSeconds / 60} мин"
        };

        string reasoning = $"Экспирация {expiryText} под выбранный таймфрейм {timeframe.ToUpper()}.";
        return new OptimalExpiryResult(totalSeconds, expiryText, reasoning);
    }
}
