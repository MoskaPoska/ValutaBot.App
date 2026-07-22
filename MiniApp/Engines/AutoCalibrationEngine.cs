using System.Collections.Concurrent;

namespace ValutaBot.MiniApp;

/// <summary>
/// Market-Regime Aware Auto-Calibrating Signal Weight Engine for Forex & OTC market pairs.
/// Classifies current market phase ("TRENDING_IMPULSE", "RANGING_FLAT", "HIGH_VOLATILITY_CHAOS")
/// and applies adaptive regime weight matrices combined with rolling empirical win-rate statistics.
/// </summary>
public static class AutoCalibrationEngine
{
    public enum MarketRegime
    {
        TrendingImpulse,
        RangingFlat,
        HighVolatilityChaos
    }

    private class SourceStats
    {
        public int Wins { get; set; }
        public int Losses { get; set; }
        public int Total => Wins + Losses;
        public double WinRate => Total > 0 ? (double)Wins / Total : 0.65;
    }

    private static readonly ConcurrentDictionary<string, SourceStats> _statsMap = new();

    public static MarketRegime DetectMarketRegime(double adx, double volRatio, double rsi)
    {
        if (volRatio > 1.75 || Math.Abs(rsi - 50.0) > 28.0)
        {
            return MarketRegime.HighVolatilityChaos;
        }

        if (adx >= 24.0)
        {
            return MarketRegime.TrendingImpulse;
        }

        return MarketRegime.RangingFlat;
    }

    public static double GetCalibratedRegimeWeight(
        string sourceName,
        string asset,
        string timeframe,
        double adx,
        double volRatio,
        double rsi,
        double defaultBaseWeight = 1.0)
    {
        var regime = DetectMarketRegime(adx, volRatio, rsi);

        // ─── 1. Apply Market Regime Preset Base Weight ───
        double regimeBaseWeight = (regime, sourceName.ToUpper()) switch
        {
            // TRENDING IMPULSE: SMC & OrderFlow dominate
            (MarketRegime.TrendingImpulse, "SMC") => 2.20,
            (MarketRegime.TrendingImpulse, "ORDERFLOW") => 2.00,
            (MarketRegime.TrendingImpulse, "LIGHTGBM") => 1.50,
            (MarketRegime.TrendingImpulse, "CLAUDE_AI") => 1.40,
            (MarketRegime.TrendingImpulse, "SKENDER_MATH") => 0.80,

            // RANGING FLAT: Skender Math (Connors RSI/HMA) & Claude AI dominate
            (MarketRegime.RangingFlat, "SKENDER_MATH") => 2.20,
            (MarketRegime.RangingFlat, "CLAUDE_AI") => 1.60,
            (MarketRegime.RangingFlat, "SMC") => 1.20,
            (MarketRegime.RangingFlat, "ORDERFLOW") => 0.50,
            (MarketRegime.RangingFlat, "LIGHTGBM") => 0.60,

            // HIGH VOLATILITY CHAOS: OrderFlow Absorption & Skender Math dominate
            (MarketRegime.HighVolatilityChaos, "ORDERFLOW") => 2.20,
            (MarketRegime.HighVolatilityChaos, "SKENDER_MATH") => 1.80,
            (MarketRegime.HighVolatilityChaos, "SMC") => 1.00,
            (MarketRegime.HighVolatilityChaos, "CLAUDE_AI") => 1.00,
            (MarketRegime.HighVolatilityChaos, "LIGHTGBM") => 0.50,

            _ => defaultBaseWeight
        };

        // ─── 2. Apply Rolling Empirical Win Rate Multiplier ───
        string key = $"{sourceName.ToUpper()}_{asset.ToUpper()}_{timeframe.ToLower()}";
        if (!_statsMap.TryGetValue(key, out var stats) || stats.Total < 5)
        {
            string fallbackKey = $"{sourceName.ToUpper()}_GLOBAL";
            _statsMap.TryGetValue(fallbackKey, out stats);
        }

        double winRate = stats != null && stats.Total >= 5 ? stats.WinRate : 0.65;
        double winRateMultiplier = winRate switch
        {
            >= 0.80 => 1.6,
            >= 0.70 => 1.3,
            >= 0.55 => 1.0,
            >= 0.45 => 0.7,
            _ => 0.4
        };

        double finalWeight = Math.Round(regimeBaseWeight * winRateMultiplier, 2);
        BotLogger.Info($"[AutoCalibration] {sourceName} ({asset} {timeframe}) | Regime: {regime} | Base: {regimeBaseWeight:F2}x | WR: {winRate * 100:F1}% -> Final Weight: {finalWeight}x");
        return finalWeight;
    }

    public static void RecordSourceOutcome(string sourceName, string asset, string timeframe, bool isWin)
    {
        string specificKey = $"{sourceName.ToUpper()}_{asset.ToUpper()}_{timeframe.ToLower()}";
        string globalKey = $"{sourceName.ToUpper()}_GLOBAL";

        UpdateStats(_statsMap.GetOrAdd(specificKey, _ => new SourceStats()), isWin);
        UpdateStats(_statsMap.GetOrAdd(globalKey, _ => new SourceStats()), isWin);
    }

    private static void UpdateStats(SourceStats stats, bool isWin)
    {
        lock (stats)
        {
            if (isWin) stats.Wins++;
            else stats.Losses++;

            if (stats.Total > 50)
            {
                stats.Wins = (int)(stats.Wins * 0.9);
                stats.Losses = (int)(stats.Losses * 0.9);
            }
        }
    }
}
