using System.IO;
using System.Linq;

string path = @"MiniApp\Engines\SmcEngine.cs";
var lines = File.ReadAllLines(path).ToList();

for(int i=0; i<lines.Count; i++) {
    if (lines[i].Contains("candles.Take(n - 3).TakeLast(15).Max(c => c.High);")) {
        lines[i] = @"        int spanStart = System.Math.Max(0, n - 18);
        int spanLen = System.Math.Min(15, n - 3 - spanStart);
        var recentCandles = candles.Slice(spanStart, spanLen);
        double recentHigh = double.MinValue;
        double recentLow = double.MaxValue;
        foreach (var c in recentCandles)
        {
            if (c.High > recentHigh) recentHigh = c.High;
            if (c.Low < recentLow) recentLow = c.Low;
        }";
    } else if (lines[i].Contains("candles.Take(n - 3).TakeLast(15).Min(c => c.Low);")) {
        lines[i] = "";
    }
}
File.WriteAllLines(path, lines);
