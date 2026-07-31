using System;
using System.IO;

string path = @"MiniApp\Engines\MathIndicatorsLibrary.cs";
string content = File.ReadAllText(path);

// we want to remove ComputeKalmanFilter, ComputeDeMarkScore and CalculateZeroLagKalman.
// They are exactly at specific indices. We will just use string replace.

int k1Start = content.IndexOf("public static double[] ComputeKalmanFilter(ReadOnlySpan<double> prices)");
int k1End = content.IndexOf("public static double CalculateShannonEntropy(ReadOnlySpan<double> prices, int bins = 10)");

if (k1Start != -1 && k1End != -1) {
    // Remove the block between k1Start and k1End
    content = content.Remove(k1Start, k1End - k1Start);
}

int zStart = content.IndexOf("/// <summary>\r\n    /// Zero-Lag 1D Kalman Filter");
if (zStart == -1) zStart = content.IndexOf("/// <summary>\n    /// Zero-Lag 1D Kalman Filter");

if (zStart != -1) {
    // Keep everything up to zStart and append }
    content = content.Substring(0, zStart) + "}\n";
}

File.WriteAllText(path, content);
