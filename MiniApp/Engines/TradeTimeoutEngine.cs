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
        string dynamicReason = "Base timeout applied (15 candles).";

        // If ATR is extremely low relative to average price, the market is completely dead.
        // We cut the trade very fast to free up margin. (Assuming ATR < 0.05% of asset price roughly implies a dead market for scalping)
        // Note: ATR isn't normalized by price here natively, so we use a heuristic based on VolRatio primarily, 
        // but if ATR is practically 0, we can drop it.
        bool isDeadMarket = atr < 0.00001; // absolute zero check fallback
        
        if (isDeadMarket || volRatio < 0.3)
        {
            baseCandles = 5;
            dynamicReason = "Dead market detected (VolRatio < 0.3 or zero ATR). Extreme fast timeout applied (5 candles).";
        }
        else if (volRatio > 1.5)
        {
            // High volatility -> price should reach target faster. Less patience for stagnation.
            baseCandles = 10;
            dynamicReason = "High Volatility Regime (VolRatio > 1.5). Price should reach target faster. Reduced timeout (10 candles).";
        }
        else if (volRatio < 0.8)
        {
            // Low volatility -> market is slow, needs more time to traverse ATR distance.
            baseCandles = 25;
            dynamicReason = "Low Volatility Regime (VolRatio < 0.8). Market is slow, extended patience required (25 candles).";
        }

        // Structural modification
        if (smc.HasOrderBlock || smc.HasFvg)
        {
            // If entering an OB, the reaction must be sharp and immediate. 
            // Lingering in an OB means it is likely failing.
            baseCandles = (int)(baseCandles * 0.6);
            if (baseCandles < 3) baseCandles = 3; // Reduced minimum from 5 to 3 for ultra-fast scalps
            dynamicReason += " | SMC Alert: Entered at OrderBlock/FVG. Reaction must be immediate. Timeout cut by 40%.";
        }

        string timeoutText = $"{baseCandles} candles";
        string reasoning = $"Timeout: {timeoutText}. {dynamicReason}";

        return new TimeoutResult(baseCandles, timeoutText, reasoning);
    }
}
