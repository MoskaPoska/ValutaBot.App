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
        int baseSeconds = MarketDataFetcher.TimeframeSeconds(timeframe);
        string tfLower = timeframe.ToLower().Trim();

        // ─── 1. Sub-minute timeframes (s5, s10, s15, s30) ───
        if (isSubMinute)
        {
            int subSec = tfLower switch
            {
                "s3" => 10,
                "s5" => 15,
                "s10" => 30,
                "s15" => 45,
                "s30" => 60,
                _ => 30
            };

            if (smc.HasLiquiditySweep)
            {
                // Quick impulse reversal -> short expiry to capture quick spike
                subSec = (int)(subSec * 0.8);
                return new OptimalExpiryResult(subSec, $"{subSec} сек", "Импульсный отскок от снятия ликвидности (быстрый вход).");
            }
            if (smc.HasFvg)
            {
                // FVG rebalance filling -> medium expiry
                return new OptimalExpiryResult(subSec, $"{subSec} сек", "Заполнение FVG имбаланса в реальном времени.");
            }

            return new OptimalExpiryResult(subSec, $"{subSec} сек", "Базовая субминутная экспирация под текущий спред.");
        }

        // ─── 2. Standard timeframes (m1, m2, m5, m15) ───
        int multiplier = (tfLower is "m1" or "1m") ? 1 : 2; // M1 defaults to 1 candle (1m), M5 defaults to 2 candles

        // Volatility Adjustment (ATR / Volatility Ratio)
        if (volRatio > 1.4)
        {
            // High volatility -> Shorter duration to avoid sudden reversals
            multiplier = 1;
        }
        else if (volRatio < 0.7)
        {
            // Low volatility -> Longer duration to give price time to hit target
            multiplier = 3;
        }

        // SMC Pattern Specific Adjustments
        string smcNote = "";
        if (smc.HasLiquiditySweep)
        {
            multiplier = Math.Max(1, multiplier - 1);
            smcNote = "Снятие ликвидности (быстрый импульсный разворот).";
        }
        else if (smc.HasOrderBlock)
        {
            multiplier += 1;
            smcNote = "Тест институционального Order Block (требуется время на ретест).";
        }
        else if (smc.HasFvg)
        {
            smcNote = "Перекрытие FVG имбаланса.";
        }

        // Active Forex Session Adjustment (UTC Time)
        int currentHourUtc = DateTime.UtcNow.Hour;
        bool isAsianSession = currentHourUtc >= 22 || currentHourUtc < 7; // Quiet Asian session
        if (isAsianSession && !asset.Contains("JPY") && !asset.Contains("AUD") && !asset.Contains("NZD"))
        {
            // Low liquidity during Asian session for EUR/USD, GBP/USD -> add time
            multiplier += 1;
        }

        int totalSeconds = baseSeconds * multiplier;
        totalSeconds = Math.Clamp(totalSeconds, 30, 900); // Between 30s and 15m

        string expiryText = totalSeconds switch
        {
            < 60 => $"{totalSeconds} сек",
            60 => "1 минута",
            120 => "2 минуты",
            180 => "3 минуты",
            240 => "4 минуты",
            300 => "5 минут",
            _ => $"{totalSeconds / 60} мин"
        };

        string fullReasoning = !string.IsNullOrEmpty(smcNote)
            ? $"Рекомендация: {expiryText}. {smcNote}"
            : $"Рекомендация: {expiryText}. Оптимальная длительность под текущую волатильность (VolRatio: {volRatio:F1}).";

        return new OptimalExpiryResult(totalSeconds, expiryText, fullReasoning);
    }
}
