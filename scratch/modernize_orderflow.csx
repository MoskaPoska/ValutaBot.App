using System;
using System.IO;

string path = @"MiniApp\Engines\OrderFlowEngine.cs";
string content = File.ReadAllText(path);

content = content.Replace(
    "double[] prices,\r\n        double[] volumes,\r\n        MiniAppController.OhlcCandle[]? candles = null",
    "ReadOnlySpan<double> prices,\r\n        ReadOnlySpan<double> volumes,\r\n        ReadOnlySpan<MiniAppController.OhlcCandle> candles = default"
);

content = content.Replace("candles != null", "!candles.IsEmpty");
content = content.Replace(
    "double avgVolume = volumes.Take(n - 1).TakeLast(20).Where(v => v > 0).DefaultIfEmpty(1.0).Average();",
    @"int spanStart = Math.Max(0, n - 21);
        int spanLen = Math.Min(20, n - 1 - spanStart);
        var volWindow = volumes.Slice(spanStart, spanLen);
        double sum = 0;
        int count = 0;
        foreach (var v in volWindow)
        {
            if (v > 0)
            {
                sum += v;
                count++;
            }
        }
        double avgVolume = count > 0 ? sum / count : 1.0;"
);

File.WriteAllText(path, content);
