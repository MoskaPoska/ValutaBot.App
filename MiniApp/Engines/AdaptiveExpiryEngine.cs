namespace ValutaBot.MiniApp;

/// <summary>
/// Adaptive Expiry Calculation Engine for Forex & OTC Currency Pairs.
/// Dynamically calculates the optimal trade duration (expiry time in seconds and minutes)
/// based on ATR volatility, SMC pattern type (FVG vs OB vs Sweep), active Forex session, and timeframe.
/// </summary>
public class AdaptiveExpiryEngine : IAdaptiveExpiryEngine
{
    public AdaptiveExpiryEngine()
    {
    }
    public record OptimalExpiryResult(
        int ExpirySeconds,
        string ExpiryText,
        string Reasoning
    );

    public OptimalExpiryResult CalculateOptimalExpiry(
        string asset,
        string timeframe,
        double atr,
        double volRatio,
        SmcEngine.SmcAnalysisResult smc,
        bool isSubMinute)
    {
        string tfLower = timeframe.ToLower().Trim();
        int baseSeconds = tfLower switch
        {
            "s3" => 3, "s5" => 5, "s10" => 10, "s15" => 15, "s30" => 30,
            "m1" or "1m" => 60, "m2" or "2m" => 120, "m3" or "3m" => 180,
            "m5" or "5m" => 300, "m15" or "15m" => 900, "m30" or "30m" => 1800,
            "h1" or "1h" => 3600, "h4" or "4h" => 14400, _ => 60
        };

        // Dynamic adjustment based on ATR / Volatility
        double multiplier = 1.0;
        string dynamicReason = "Стандартная волатильность.";

        if (volRatio > 1.5)
        {
            multiplier = 2.0;
            dynamicReason = "Рынок турбулентный (VolRatio > 1.5) — удвоенная экспирация.";
        }
        else if (smc != null && (smc.HasOrderBlock || smc.HasFvg))
        {
            multiplier = 1.5;
            dynamicReason = "Цена в зоне SMC (Ордерблок/FVG) — увеличено время на отработку.";
        }

        int totalSeconds = (int)(baseSeconds * multiplier);
        if (totalSeconds < 5) totalSeconds = 5;
        if (totalSeconds > 14400) totalSeconds = 14400;

        string expiryText;
        if (totalSeconds < 60)
        {
            expiryText = $"{totalSeconds} сек";
        }
        else
        {
            int m = totalSeconds / 60;
            int s = totalSeconds % 60;
            expiryText = s > 0 ? $"{m} мин {s} сек" : m switch
            {
                1 => "1 минута",
                2 => "2 минуты",
                3 => "3 минуты",
                4 => "4 минуты",
                5 => "5 минут",
                15 => "15 минут",
                30 => "30 минут",
                60 => "1 час",
                _ => $"{m} мин"
            };
        }

        string reasoning = $"Экспирация {expiryText} под таймфрейм {timeframe.ToUpper()}. {dynamicReason}";
        return new OptimalExpiryResult(totalSeconds, expiryText, reasoning);
    }
}



