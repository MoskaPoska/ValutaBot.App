using System;

namespace ValutaBot.MiniApp;

/// <summary>
/// Time-based Risk Engine (Trade Timeout Engine).
/// Calculates the optimal number of candles to hold a position before it becomes statistically disadvantageous (Stagnant Trade).
/// If a trade does not reach structural targets (TP/SL) within this timeout, it is forced to close to protect margin.
/// </summary>
public class TradeTimeoutEngine : ITradeTimeoutEngine
{
    public record TimeoutResult(
        int TimeoutCandles,
        string TimeoutText,
        string Reasoning
    );

    public TimeoutResult CalculateTimeout(
        string asset,
        string timeframe,
        double atr,
        double volRatio,
        SmcEngine.SmcAnalysisResult smc)
    {
        int baseCandles = 15;
        string dynamicReason = "Стандартный лимит консолидации (15 свечей).";

        if (volRatio > 1.5)
        {
            // High volatility -> price should reach target faster. Less patience for stagnation.
            baseCandles = 10;
            dynamicReason = "Высокая волатильность (VolRatio > 1.5) — таймаут сокращен до 10 свечей (ожидаем быстрый пробой).";
        }
        else if (volRatio < 0.8)
        {
            // Low volatility -> market is slow, needs more time to traverse ATR distance.
            baseCandles = 25;
            dynamicReason = "Низкая волатильность (VolRatio < 0.8) — таймаут расширен до 25 свечей.";
        }

        // Structural modification
        if (smc.HasOrderBlock || smc.HasFvg)
        {
            // If entering an OB, the reaction must be sharp and immediate. 
            // Lingering in an OB means it is likely failing.
            baseCandles = (int)(baseCandles * 0.6);
            if (baseCandles < 5) baseCandles = 5;
            dynamicReason += " | Вход от зоны SMC (OB/FVG) требует немедленной реакции — таймаут усечен на 40%.";
        }

        string timeoutText = $"{baseCandles} свечей";
        string reasoning = $"Тайм-стоп: {timeoutText}. {dynamicReason}";

        return new TimeoutResult(baseCandles, timeoutText, reasoning);
    }
}
