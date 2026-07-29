using System;

namespace ValutaBot.MiniApp;

/// <summary>
/// Mathematical metrics for Chaos Theory, Fractal Geometry, and specialized transformations.
/// Core Technical Analysis (EMA, RSI, MACD, etc.) is handled by TechnicalAnalysisEngine.Instance.cs using Skender.Stock.Indicators.
/// </summary>
public static class MathIndicatorsLibrary
{
    /// <summary>
    /// Calculates the Hurst Exponent (H) of a price series using Rescaled Range / Variance Ratio approximation.
    /// H < 0.5: Mean-reverting series (anti-persistent)
    /// H ~ 0.5: Random walk
    /// H > 0.5: Trending series (persistent)
    /// </summary>
    public static double CalculateHurstExponent(ReadOnlySpan<double> prices)
    {
        int n = prices.Length;
        if (n < 30) return 0.5;

        Span<int> scales = stackalloc int[] { 2, 4, 8, 16, 32 };
        Span<double> logScales = stackalloc double[5];
        Span<double> logRs = stackalloc double[5];
        int count = 0;

        foreach (int scale in scales)
        {
            if (scale > n / 2) continue;

            int diffCount = 0;
            double sum = 0.0;
            
            for (int i = scale; i < n; i += scale)
            {
                sum += prices[i] - prices[i - scale];
                diffCount++;
            }

            if (diffCount < 2) continue;

            double mean = sum / diffCount;
            double sumSq = 0.0;

            for (int i = scale; i < n; i += scale)
            {
                double dev = (prices[i] - prices[i - scale]) - mean;
                sumSq += dev * dev;
            }

            double std = Math.Sqrt(sumSq / (diffCount - 1));
            
            if (std > 1e-12)
            {
                logScales[count] = Math.Log(scale);
                logRs[count] = Math.Log(std);
                count++;
            }
        }

        if (count < 3) return 0.5;

        // Simple Linear Regression to find Slope (Hurst Exponent)
        double sumX = 0, sumY = 0;
        for (int i = 0; i < count; i++)
        {
            sumX += logScales[i];
            sumY += logRs[i];
        }
        double meanX = sumX / count;
        double meanY = sumY / count;

        double num = 0, den = 0;
        for (int i = 0; i < count; i++)
        {
            double dx = logScales[i] - meanX;
            num += dx * (logRs[i] - meanY);
            den += dx * dx;
        }

        double hurst = den == 0 ? 0 : num / den;
        return Math.Clamp(hurst, 0.0, 1.0);
    }

    public static double[] ComputeKalmanFilter(ReadOnlySpan<double> prices)
    {
        int n = prices.Length;
        if (n == 0) return Array.Empty<double>();
        
        double[] filtered = new double[n];
        double processNoise = 0.01;
        double measurementNoise = 0.1;

        double est = prices[0];
        double err = 1.0;

        for (int i = 0; i < n; i++)
        {
            double k = err / (err + measurementNoise);
            est = est + k * (prices[i] - est);
            err = (1.0 - k) * err + processNoise;
            filtered[i] = est;
        }

        return filtered;
    }

    public static double ComputeDeMarkScore(ReadOnlySpan<double> prices)
    {
        if (prices.Length < 15) return 0;
        
        int buySetup = 0;
        int sellSetup = 0;

        for (int i = 4; i < prices.Length; i++)
        {
            if (prices[i] < prices[i - 4])
            {
                buySetup++;
                sellSetup = 0;
            }
            else if (prices[i] > prices[i - 4])
            {
                sellSetup++;
                buySetup = 0;
            }
            else
            {
                buySetup = 0;
                sellSetup = 0;
            }
        }

        if (buySetup >= 9) return 0.35;
        if (sellSetup >= 9) return -0.35;
        
        return 0.0;
    }
}
