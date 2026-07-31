using System;
using System.IO;

string path = @"MiniApp\Engines\AutoCalibrationEngine.cs";

string fullFile = @"using System.Collections.Concurrent;
using System.Globalization;
using System;

namespace ValutaBot.MiniApp;

/// <summary>
/// Market-Regime Aware Auto-Calibrating Signal Weight Engine for Forex & OTC market pairs.
/// Classifies current market phase (""TRENDING_IMPULSE"", ""RANGING_FLAT"", ""HIGH_VOLATILITY_CHAOS"")
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

    public static MarketRegime DetectMarketRegime(double adx, double volRatio, double rsi, ReadOnlySpan<double> prices = default)
    {
        double entropy = prices.IsEmpty ? 0.5 : MathIndicatorsLibrary.CalculateShannonEntropy(prices);

        if (adx >= 25.0 && volRatio <= 2.0 && entropy < 0.75)
        {
            return MarketRegime.TrendingImpulse;
        }

        if (entropy > 0.90 || volRatio > 1.75 || Math.Abs(rsi - 50.0) > 28.0)
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
        double defaultBaseWeight = 1.0,
        ReadOnlySpan<double> prices = default)
    {
        ArgumentNullException.ThrowIfNull(sourceName);
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentNullException.ThrowIfNull(timeframe);

        var regime = DetectMarketRegime(adx, volRatio, rsi, prices);

        // 1. Regime Base Weight Matrix
        double regimeMultiplier = 1.0;
        
        switch (regime)
        {
            case MarketRegime.TrendingImpulse:
                if (sourceName == ""ONNX"") regimeMultiplier = 1.40;
                else if (sourceName == ""LIGHTGBM"") regimeMultiplier = 1.30;
                else if (sourceName == ""SKENDER_MATH"") regimeMultiplier = 0.80; // Math lags in strong trends
                else regimeMultiplier = 1.0;
                break;
                
            case MarketRegime.RangingFlat:
                if (sourceName == ""ONNX"") regimeMultiplier = 0.90; // ONNX overshoots in ranging
                else if (sourceName == ""LIGHTGBM"") regimeMultiplier = 1.10;
                else if (sourceName == ""SKENDER_MATH"") regimeMultiplier = 1.40; // Math oscillators shine here
                else regimeMultiplier = 1.0;
                break;
                
            case MarketRegime.HighVolatilityChaos:
                if (sourceName == ""ONNX"") regimeMultiplier = 0.60; // AI hallucinates in chaos
                else if (sourceName == ""LIGHTGBM"") regimeMultiplier = 0.70;
                else if (sourceName == ""SKENDER_MATH"") regimeMultiplier = 0.90; // Fallback to conservative math
                else regimeMultiplier = 1.0;
                break;
        }

        double baseWeight = defaultBaseWeight * regimeMultiplier;

        // 2. Rolling Empirical Win-Rate Calibration
        string statsKey = $""{sourceName}_{asset}_{timeframe}"";
        if (_statsMap.TryGetValue(statsKey, out var stats) && stats.Total >= 10)
        {
            double wr = stats.WinRate;
            
            // Sigmoid-style non-linear reward/punishment
            double calibrationFactor = 1.0;
            if (wr > 0.65) calibrationFactor = 1.0 + (wr - 0.65) * 2.5; // Exponential reward for >65%
            else if (wr < 0.45) calibrationFactor = 0.5 + (wr / 0.45) * 0.5; // Harsh penalty for <45%
            
            return Math.Round(baseWeight * calibrationFactor, 2);
        }

        return Math.Round(baseWeight, 2);
    }

    public static void RecordTradeOutcome(string sourceName, string asset, string timeframe, bool isWin)
    {
        string statsKey = $""{sourceName}_{asset}_{timeframe}"";
        var stats = _statsMap.GetOrAdd(statsKey, _ => new SourceStats());

        lock (stats)
        {
            if (isWin) stats.Wins++;
            else stats.Losses++;
            stats.TotalTrades++;

            // Window sliding to keep recent relevance (decay)
            if (stats.Total > 200)
            {
                stats.Wins = (int)(stats.Wins * 0.9);
                stats.Losses = (int)(stats.Losses * 0.9);
            }
        }
    }

    public static string GetStatsReport(string sourceName, string asset, string timeframe)
    {
        string statsKey = $""{sourceName}_{asset}_{timeframe}"";
        if (_statsMap.TryGetValue(statsKey, out var stats))
        {
            return $""[Auto-Calib] {sourceName}: W={stats.Wins} L={stats.Losses} (WR: {stats.WinRate:P1})"";
        }
        return $""[Auto-Calib] {sourceName}: Нет данных"";
    }
}
";

File.WriteAllText(path, fullFile);
Console.WriteLine("Fixed!");
