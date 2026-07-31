using System;
using System.IO;

string path = @"MiniApp\Engines\SmcEngine.cs";
string content = File.ReadAllText(path);

// Replace AnalyzeSmcStructure signature
content = content.Replace(
    "MiniAppController.OhlcCandle[] candles, double currentPrice",
    "ReadOnlySpan<MiniAppController.OhlcCandle> candles, double currentPrice"
);

// Replace LINQ logic for recentHigh and recentLow
string oldLinq = @"double recentHigh = candles.Take(n - 3).TakeLast(15).Max(c => c.High);
        double recentLow = candles.Take(n - 3).TakeLast(15).Min(c => c.Low);";

string newSpan = @"int spanStart = Math.Max(0, n - 18);
        int spanLen = Math.Min(15, n - 3 - spanStart);
        var recentCandles = candles.Slice(spanStart, spanLen);
        double recentHigh = double.MinValue;
        double recentLow = double.MaxValue;
        foreach (var c in recentCandles)
        {
            if (c.High > recentHigh) recentHigh = c.High;
            if (c.Low < recentLow) recentLow = c.Low;
        }";

content = content.Replace(oldLinq, newSpan);

// The caller in MarketAnalysisContext passes _ohlcCandles (which is an array) and it implicitly casts to ReadOnlySpan.

File.WriteAllText(path, content);
