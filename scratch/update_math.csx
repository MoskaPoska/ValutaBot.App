using System.IO;

string path = @"MiniApp\Engines\MathIndicatorsLibrary.cs";
string content = File.ReadAllText(path);

string newMethods = @"
    /// <summary>
    /// Computes Shannon Entropy (Information Theory) of price returns to measure market randomness.
    /// High Entropy = Chaotic/Efficient Market (hard to predict). Low Entropy = Trending/Inefficient Market.
    /// </summary>
    public static double CalculateShannonEntropy(ReadOnlySpan<double> prices, int bins = 10)
    {
        int len = Math.Min(prices.Length, 1000); // Max 1000 to keep stackalloc safe (< 8KB)
        if (len < 10) return 1.0;

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
        
        // Normalize entropy between 0 and 1
        double maxEntropy = Math.Log2(bins);
        return entropy / maxEntropy;
    }

    /// <summary>
    /// Zero-Lag 1D Kalman Filter for state-space price smoothing.
    /// Q = Process Noise, R = Measurement Noise.
    /// </summary>
    public static double CalculateZeroLagKalman(ReadOnlySpan<double> prices, double q = 0.0001, double r = 0.01)
    {
        if (prices.Length == 0) return 0;
        
        double x = prices[0]; // State estimate
        double p = 1.0;       // Estimate covariance

        for (int i = 1; i < prices.Length; i++)
        {
            // Prediction update
            p = p + q;
            
            // Measurement update
            double k = p / (p + r); // Kalman gain
            x = x + k * (prices[i] - x);
            p = (1.0 - k) * p;
        }
        
        return x;
    }
}
";

content = content.TrimEnd();
if (content.EndsWith("}")) {
    content = content.Substring(0, content.Length - 1) + newMethods;
    File.WriteAllText(path, content);
    Console.WriteLine("Success");
}
