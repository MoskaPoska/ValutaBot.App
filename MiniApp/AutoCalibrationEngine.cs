using System.Collections.Concurrent;

namespace ValutaBot.MiniApp;

/// <summary>
/// Auto-Calibrating Signal Weight Engine for Forex & OTC market pairs.
/// Automatically adjusts signal source weights based on real-time empirical Win Rate history.
/// </summary>
public static class AutoCalibrationEngine
{
    private class SourceStats
    {
        public int Wins { get; set; }
        public int Losses { get; set; }
        public int Total => Wins + Losses;
        public double WinRate => Total > 0 ? (double)Wins / Total : 0.65; // Default 65% base assumption
    }

    private static readonly ConcurrentDictionary<string, SourceStats> _statsMap = new();

    public static double GetCalibratedWeight(string sourceName, string asset, string timeframe, double baseWeight = 1.0)
    {
        string key = $"{sourceName.ToUpper()}_{asset.ToUpper()}_{timeframe.ToLower()}";
        if (!_statsMap.TryGetValue(key, out var stats) || stats.Total < 5)
        {
            // If less than 5 records, use asset-agnostic source key
            string fallbackKey = $"{sourceName.ToUpper()}_GLOBAL";
            if (!_statsMap.TryGetValue(fallbackKey, out stats) || stats.Total < 5)
            {
                return baseWeight;
            }
        }

        double wr = stats.WinRate;
        double multiplier = wr switch
        {
            >= 0.80 => 2.0,
            >= 0.70 => 1.5,
            >= 0.55 => 1.0,
            >= 0.45 => 0.7,
            _ => 0.4
        };

        double finalWeight = Math.Round(baseWeight * multiplier, 2);
        BotLogger.Info($"[AutoCalibration] {sourceName} ({asset} {timeframe}): WinRate={wr * 100:F1}% ({stats.Wins}/{stats.Total}) -> Dynamic Weight={finalWeight}x");
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

            // Keep rolling window of last 50 outcomes
            if (stats.Total > 50)
            {
                stats.Wins = (int)(stats.Wins * 0.9);
                stats.Losses = (int)(stats.Losses * 0.9);
            }
        }
    }
}
