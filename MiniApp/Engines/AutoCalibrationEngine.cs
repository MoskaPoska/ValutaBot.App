using System.Collections.Concurrent;
using System.Globalization;
using System;

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

    public static MarketRegime DetectMarketRegime(double adx, double volRatio, double rsi, ReadOnlySpan<double> prices = default)
    {
        double entropy = prices.IsEmpty ? 0.5 : CalculateShannonEntropy(prices);

        // Dominant regime classification (used for logging/labels)
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

    private static double CalculateShannonEntropy(ReadOnlySpan<double> prices)
    {
        int len = Math.Min(prices.Length, 1000);
        if (len < 10) return 1.0;

        // Sturges' rule for optimal number of bins
        int bins = (int)Math.Ceiling(1.0 + Math.Log2(len));

        Span<double> returns = stackalloc double[len - 1];
        double minRet = double.MaxValue;
        double maxRet = double.MinValue;

        int startIndex = prices.Length - len;
        for (int i = 1; i < len; i++)
        {
            double prev = prices[startIndex + i - 1];
            double curr = prices[startIndex + i];
            double ret = prev != 0 ? (curr - prev) / prev : 0;
            returns[i - 1] = ret;
            if (ret < minRet) minRet = ret;
            if (ret > maxRet) maxRet = ret;
        }

        if (maxRet - minRet < 1e-9) return 0.0;

        Span<int> histogram = stackalloc int[bins];
        double binWidth = (maxRet - minRet) / bins;

        foreach (var r in returns)
        {
            int bin = (int)((r - minRet) / binWidth);
            if (bin >= bins) bin = bins - 1;
            histogram[bin]++;
        }

        double entropy = 0.0;
        int totalReturns = returns.Length;
        foreach (var count in histogram)
        {
            if (count > 0)
            {
                double p = (double)count / totalReturns;
                entropy -= p * Math.Log2(p);
            }
        }
        
        double maxEntropy = Math.Log2(bins);
        return entropy / maxEntropy;
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

        var dominantRegime = DetectMarketRegime(adx, volRatio, rsi, prices);

        // 1. Fuzzy Regime Base Weight Matrix
        // Instead of hard-switching, we calculate a continuous blending score (0.0 to 1.0)
        double trendScore = Math.Clamp((adx - 15.0) / 20.0, 0.0, 1.0); // 15 ADX = 0, 35 ADX = 1
        double chaosScore = Math.Clamp(prices.IsEmpty ? 0.5 : CalculateShannonEntropy(prices), 0.0, 1.0);
        double flatScore  = Math.Clamp(1.0 - trendScore - (chaosScore * 0.5), 0.0, 1.0);

        double regimeMultiplier = 1.0;
        
        if (sourceName == "LIGHTGBM")
        {
            // LightGBM performs best in trends, okay in flat, bad in chaos
            regimeMultiplier = (trendScore * 1.35) + (flatScore * 1.15) + (chaosScore * 0.70);
        }
        else if (sourceName == "SKENDER_MATH")
        {
            // Skender Math performs well in flat and trend, bad in chaos
            regimeMultiplier = (trendScore * 1.25) + (flatScore * 1.10) + (chaosScore * 0.80);
        }

        double baseWeight = defaultBaseWeight * regimeMultiplier;

        // 2. Rolling Empirical Win-Rate Calibration
        string statsKey = $"{sourceName}_{asset}_{timeframe}";
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

    public static void RecordSourceOutcome(string sourceName, string asset, string timeframe, bool isWin)
    {
        string statsKey = $"{sourceName}_{asset}_{timeframe}";
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
        string statsKey = $"{sourceName}_{asset}_{timeframe}";
        if (_statsMap.TryGetValue(statsKey, out var stats))
        {
            return $"[Auto-Calib] {sourceName}: W={stats.Wins} L={stats.Losses} (WR: {stats.WinRate:P1})";
        }
        return $"[Auto-Calib] {sourceName}: ��� ������";
    }
}
