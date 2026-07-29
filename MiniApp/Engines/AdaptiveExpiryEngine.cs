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
        string dynamicReason = "РЎС‚Р°РЅРґР°СЂС‚РЅР°СЏ РІРѕР»Р°С‚РёР»СЊРЅРѕСЃС‚СЊ.";

        if (volRatio > 1.5)
        {
            multiplier = 2.0;
            dynamicReason = "Р С‹РЅРѕРє С‚СѓСЂР±СѓР»РµРЅС‚РЅС‹Р№ (VolRatio > 1.5) вЂ” СѓРґРІРѕРµРЅРЅР°СЏ СЌРєСЃРїРёСЂР°С†РёСЏ.";
        }
        else if (smc != null && (smc.HasOrderBlock || smc.HasFvg))
        {
            multiplier = 1.5;
            dynamicReason = "Р¦РµРЅР° РІ Р·РѕРЅРµ SMC (РћСЂРґРµСЂР±Р»РѕРє/FVG) вЂ” СѓРІРµР»РёС‡РµРЅРѕ РІСЂРµРјСЏ РЅР° РѕС‚СЂР°Р±РѕС‚РєСѓ.";
        }

        int totalSeconds = (int)(baseSeconds * multiplier);
        if (totalSeconds < 5) totalSeconds = 5;
        if (totalSeconds > 14400) totalSeconds = 14400;

        string expiryText;
        if (totalSeconds < 60)
        {
            expiryText = $"{totalSeconds} СЃРµРє";
        }
        else
        {
            int m = totalSeconds / 60;
            int s = totalSeconds % 60;
            expiryText = s > 0 ? $"{m} РјРёРЅ {s} СЃРµРє" : m switch
            {
                1 => "1 РјРёРЅСѓС‚Р°",
                2 => "2 РјРёРЅСѓС‚С‹",
                3 => "3 РјРёРЅСѓС‚С‹",
                4 => "4 РјРёРЅСѓС‚С‹",
                5 => "5 РјРёРЅСѓС‚",
                15 => "15 РјРёРЅСѓС‚",
                30 => "30 РјРёРЅСѓС‚",
                60 => "1 С‡Р°СЃ",
                _ => $"{m} РјРёРЅ"
            };
        }

        string reasoning = $"Р­РєСЃРїРёСЂР°С†РёСЏ {expiryText} РїРѕРґ С‚Р°Р№РјС„СЂРµР№Рј {timeframe.ToUpper()}. {dynamicReason}";
        return new OptimalExpiryResult(totalSeconds, expiryText, reasoning);
    }
}



