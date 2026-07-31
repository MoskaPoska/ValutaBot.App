using System.IO;

string path = @"MiniApp\Engines\MathIndicatorsLibrary.cs";
string content = File.ReadAllText(path);

int idx = content.IndexOf("public static double[] ComputeKalmanFilter(ReadOnlySpan<double> prices)");
int endIdx = content.IndexOf("public static double CalculateShannonEntropy(ReadOnlySpan<double> prices, int bins = 10)");

if (idx != -1 && endIdx != -1) {
    content = content.Remove(idx, endIdx - idx);
}

int zlagIdx = content.IndexOf("/// <summary>\r\n    /// Zero-Lag 1D Kalman Filter");
if (zlagIdx == -1) zlagIdx = content.IndexOf("/// <summary>\n    /// Zero-Lag 1D Kalman Filter");

if (zlagIdx != -1) {
    content = content.Substring(0, zlagIdx) + "}\n";
}

File.WriteAllText(path, content);
