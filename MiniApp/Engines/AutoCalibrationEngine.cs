using System.Collections.Concurrent;
using System.Globalization;

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
        public int TotalTrades { get; set; }
        public int Total => Wins + Losses;
        public double WinRate => Total > 0 ? (double)Wins / Total : 0.50;
    }

    private static readonly ConcurrentDictionary<string, SourceStats> _statsMap = new();

    public static MarketRegime DetectMarketRegime(double adx, double volRatio, double rsi)
    {
        if (adx >= 25.0 && volRatio <= 2.0)
        {
            return MarketRegime.TrendingImpulse;
        }

        if (volRatio > 1.75 || Math.Abs(rsi - 50.0) > 28.0)
        {
            return MarketRegime.HighVolatilityChaos;
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
        ArgumentNullException.ThrowIfNull(sourceName);
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentNullException.ThrowIfNull(timeframe);

        var regime = DetectMarketRegime(adx, volRatio, rsi);

        // ─── 1. Apply Market Regime Preset Base Weight ───
        double regimeBaseWeight = (regime, sourceName.ToUpper(CultureInfo.InvariantCulture)) switch
        {
            // TRENDING IMPULSE: SMC & OrderFlow dominate
            (MarketRegime.TrendingImpulse, "SMC")          => 2.20,
            (MarketRegime.TrendingImpulse, "ORDERFLOW")    => 2.00,
            (MarketRegime.TrendingImpulse, "LIGHTGBM")     => 1.50,
            (MarketRegime.TrendingImpulse, "ONNX")         => 1.40,
            (MarketRegime.TrendingImpulse, "NATIVE_ML")    => 1.20,
            (MarketRegime.TrendingImpulse, "SKENDER_MATH") => 0.80,
            // Note: CLAUDE_AI removed — engine deprecated

            // RANGING FLAT: Skender Math (Connors RSI/HMA) & ONNX dominate
            (MarketRegime.RangingFlat, "SKENDER_MATH")     => 2.20,
            (MarketRegime.RangingFlat, "ONNX")             => 1.50,
            (MarketRegime.RangingFlat, "NATIVE_ML")        => 0.80,
            (MarketRegime.RangingFlat, "LIGHTGBM")         => 0.60,
            (MarketRegime.RangingFlat, "SMC")              => 1.20,
            (MarketRegime.RangingFlat, "ORDERFLOW")        => 0.50,
            // Note: CLAUDE_AI removed — engine deprecated

            // HIGH VOLATILITY CHAOS: OrderFlow Absorption & Skender Math dominate
            (MarketRegime.HighVolatilityChaos, "ORDERFLOW")    => 2.20,
            (MarketRegime.HighVolatilityChaos, "SKENDER_MATH") => 1.80,
            (MarketRegime.HighVolatilityChaos, "SMC")          => 1.00,
            (MarketRegime.HighVolatilityChaos, "ONNX")         => 0.80,
            (MarketRegime.HighVolatilityChaos, "LIGHTGBM")     => 0.50,
            (MarketRegime.HighVolatilityChaos, "NATIVE_ML")    => 0.50,
            // Note: CLAUDE_AI removed — engine deprecated

            _ => defaultBaseWeight
        };

        // ─── 2. Apply Rolling Empirical Win Rate Multiplier ───
        string key = $"{sourceName.ToUpper(CultureInfo.InvariantCulture)}_{asset.ToUpper(CultureInfo.InvariantCulture)}_{timeframe.ToLower(CultureInfo.InvariantCulture)}";
        if (!_statsMap.TryGetValue(key, out var stats) || stats.Total < 5)
        {
            string fallbackKey = $"{sourceName.ToUpper(CultureInfo.InvariantCulture)}_GLOBAL";
            _statsMap.TryGetValue(fallbackKey, out stats);
        }

        double winRate = stats != null && stats.Total >= 5 ? stats.WinRate : 0.50;
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
        ArgumentNullException.ThrowIfNull(sourceName);
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentNullException.ThrowIfNull(timeframe);

        string specificKey = $"{sourceName.ToUpper(CultureInfo.InvariantCulture)}_{asset.ToUpper(CultureInfo.InvariantCulture)}_{timeframe.ToLower(CultureInfo.InvariantCulture)}";
        string globalKey = $"{sourceName.ToUpper(CultureInfo.InvariantCulture)}_GLOBAL";

        UpdateStats(_statsMap.GetOrAdd(specificKey, _ => new SourceStats()), isWin);
        UpdateStats(_statsMap.GetOrAdd(globalKey, _ => new SourceStats()), isWin);
    }

    private static void UpdateStats(SourceStats stats, bool isWin)
    {
        lock (stats)
        {
            stats.TotalTrades++;
            if (isWin) stats.Wins++;
            else stats.Losses++;

            // Apply exponential forgetting factor every 10 records (not every record).
            // This keeps recent performance more relevant without wiping old data instantly.
            if (stats.TotalTrades > 0 && stats.TotalTrades % 10 == 0 && stats.TotalTrades >= 50)
            {
                stats.Wins   = Math.Max(stats.Wins   > 0 ? 1 : 0, (int)Math.Round(stats.Wins   * 0.9));
                stats.Losses = Math.Max(stats.Losses > 0 ? 1 : 0, (int)Math.Round(stats.Losses * 0.9));
            }
        }
    }
}
