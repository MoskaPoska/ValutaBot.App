using System;
using System.IO;
using System.Text.RegularExpressions;

string path = @"MiniApp\Engines\AutoCalibrationEngine.cs";
string content = File.ReadAllText(path);

string header = @"using System.Collections.Concurrent;
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

    public static double GetCalibratedRegimeWeight(";

content = Regex.Replace(content, @"using System.*?public static double GetCalibratedRegimeWeight\(", header, RegexOptions.Singleline);
File.WriteAllText(path, content);
Console.WriteLine("Fixed AutoCalibrationEngine");
