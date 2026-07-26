using System;
using System.Linq;
using System.Collections.Generic;

namespace ValutaBot.MiniApp;

public static class MathIndicatorsLibrary
{
    private const int RsiPeriod = 14;
    private const int EmaShort = 9;
    private const int EmaLong = 21;




    public static double[] ComputeEmaArray(double[] data, int period)
    {
        int n = data.Length;
        var ema = new double[n];
        if (n == 0) return ema;
        double k = 2.0 / (period + 1);
        ema[0] = data[0];
        for (int i = 1; i < n; i++)
            ema[i] = data[i] * k + ema[i - 1] * (1 - k);
        return ema;
    }

    public static double VolumeStrength(double[] prices, double[] volumes)
    {
        if (prices == null || prices.Length < 2 || volumes == null || volumes.Length < 10) return 0;
        int n = volumes.Length;
        double sumVol = 0;
        for (int i = n - 10; i < n; i++)
            sumVol += volumes[i];
        double avgVol = sumVol / 10.0;
        if (avgVol < 1e-9) return 0;

        double currentVol = volumes[^1];
        double prevClose = prices[^2];
        double currentClose = prices[^1];
        double change = (currentClose - prevClose) / prevClose;

        double volRatio = currentVol / avgVol;
        double direction = change > 0 ? 1 : -1;

        double volStrength = direction * Math.Min(volRatio, 2.0) / 2.0;
        return volStrength * 2;
    }

    public static (double wt1, double wt2) ComputeWaveTrend(MiniAppController.OhlcCandle[] candles, int channelLength = 10, int averageLength = 21)
    {
        if (candles == null || candles.Length < Math.Max(channelLength, averageLength) + 5)
            return (0.0, 0.0);

        int n = candles.Length;
        double[] typicalPrices = new double[n];
        for (int i = 0; i < n; i++)
        {
            typicalPrices[i] = (candles[i].High + candles[i].Low + candles[i].Close) / 3.0;
        }

        // 1. EMA of typical price
        double[] esa = ComputeEmaArray(typicalPrices, channelLength);

        // 2. Absolute deviation
        double[] absDev = new double[n];
        for (int i = 0; i < n; i++)
        {
            absDev[i] = Math.Abs(typicalPrices[i] - esa[i]);
        }

        // 3. EMA of absolute deviation
        double[] de = ComputeEmaArray(absDev, channelLength);

        // 4. Channel Index
        double[] ci = new double[n];
        for (int i = 0; i < n; i++)
        {
            ci[i] = (typicalPrices[i] - esa[i]) / (0.015 * de[i] + 1e-10);
        }

        // 5. WaveTrend 1 (WT1) = EMA of Channel Index
        double[] wt1 = ComputeEmaArray(ci, averageLength);

        // 6. WaveTrend 2 (WT2) = 4-period SMA of WT1
        double[] wt2 = new double[n];
        for (int i = 0; i < n; i++)
        {
            int start = Math.Max(0, i - 3);
            double sum = 0;
            int count = 0;
            for (int j = start; j <= i; j++)
            {
                sum += wt1[j];
                count++;
            }
            wt2[i] = sum / count;
        }

        return (wt1[^1], wt2[^1]);
    }



    public static double AnalyzeVolumeSpread(MiniAppController.OhlcCandle[] candles)
    {
        if (candles == null || candles.Length < 10) return 0.0;

        int last = candles.Length - 1;
        double spread = candles[last].High - candles[last].Low;

        // Check if we need to estimate volumes (for Forex on weekdays or missing feeds)
        bool estimateVolume = candles.Average(c => c.Volume) < 0.01;
        double[] finalVolumes = new double[candles.Length];
        
        if (estimateVolume)
        {
            // Estimate proxy volume based on candle spread relative to average spread
            double[] windowSpreads = candles.Select(c => c.High - c.Low).ToArray();
            double avgSpreadAll = windowSpreads.Average();
            for (int i = 0; i < candles.Length; i++)
            {
                double s = candles[i].High - candles[i].Low;
                double ratio = s / (avgSpreadAll + 1e-12);
                finalVolumes[i] = 100.0 * ratio + 10.0; // base activity of 10 ticks
            }
        }
        else
        {
            for (int i = 0; i < candles.Length; i++)
            {
                finalVolumes[i] = candles[i].Volume;
            }
        }

        double volume = finalVolumes[last];

        // Compute average spread and volume for context
        double[] spreads = candles.Skip(candles.Length - 10).Take(10).Select(c => c.High - c.Low).ToArray();
        double[] volumes = finalVolumes.Skip(finalVolumes.Length - 10).Take(10).ToArray();
        
        double avgSpread = spreads.Average();
        double avgVolume = volumes.Average();

        if (avgSpread < 1e-10 || avgVolume < 1e-10) return 0.0;

        double spreadRatio = spread / avgSpread;
        double volumeRatio = volume / avgVolume;

        // Volume Spread Analysis (VSA)
        if (candles[last].Close > candles[last].Open)
        {
            // High Volume + High Spread -> Strong Bullish Continuation
            if (volumeRatio > 1.3 && spreadRatio > 1.3) return 0.4;
            // High Volume + Tiny Spread -> Absorption / Exhaustion (Bearish Reversal Risk)
            if (volumeRatio > 1.4 && spreadRatio < 0.7) return -0.4;
            // Low Volume + High Spread -> Fake Bullish Breakout
            if (volumeRatio < 0.7 && spreadRatio > 1.3) return -0.3;
        }
        else
        {
            // High Volume + High Spread -> Strong Bearish Continuation
            if (volumeRatio > 1.3 && spreadRatio > 1.3) return -0.4;
            // High Volume + Tiny Spread -> Absorption / Exhaustion (Bullish Reversal Risk)
            if (volumeRatio > 1.4 && spreadRatio < 0.7) return 0.4;
            // Low Volume + High Spread -> Fake Bearish Breakout
            if (volumeRatio < 0.7 && spreadRatio > 1.3) return 0.3;
        }

        return 0.0;
    }

    public static double GetFibonacciBounce(double[] prices)
    {
        if (prices == null || prices.Length < 30) return 0.0;

        int len = Math.Min(45, prices.Length);
        var recentPrices = prices[^len..];
        double swingHigh = recentPrices.Max();
        double swingLow = recentPrices.Min();
        double range = swingHigh - swingLow;

        if (range < 1e-10) return 0.0;

        double currentPrice = prices[^1];
        bool generalTrendUp = prices[^1] > recentPrices[0];

        // Fibonacci Retracement Levels
        double fib618 = generalTrendUp ? swingHigh - 0.618 * range : swingLow + 0.618 * range;
        double fib50 = generalTrendUp ? swingHigh - 0.5 * range : swingLow + 0.5 * range;
        double fib382 = generalTrendUp ? swingHigh - 0.382 * range : swingLow + 0.382 * range;

        double tolerance = 0.02 * range;

        if (generalTrendUp)
        {
            if (Math.Abs(currentPrice - fib618) < tolerance) return 0.35;
            if (Math.Abs(currentPrice - fib50) < tolerance) return 0.25;
            if (Math.Abs(currentPrice - fib382) < tolerance) return 0.15;
        }
        else
        {
            if (Math.Abs(currentPrice - fib618) < tolerance) return -0.35;
            if (Math.Abs(currentPrice - fib50) < tolerance) return -0.25;
            if (Math.Abs(currentPrice - fib382) < tolerance) return -0.15;
        }

        return 0.0;
    }

    /* ─── True ADX (Wilders) & ATR ─── */







    /* ─── Bollinger z-score ─── */



    /* ─── RSI divergence ─── */



    /* ─── Linear regression slope ─── */



    /* ─── Scoring Engine ─── */

    

    public static double CalculateHurstExponent(double[] prices)
    {
        int n = prices.Length;
        if (n < 30) return 0.5;

        // Calculate differences at scale 2 (2-bar changes)
        var diff2 = new List<double>();
        for (int i = 2; i < n; i += 2)
        {
            diff2.Add(prices[i] - prices[i - 2]);
        }

        // Calculate differences at scale 16 (16-bar changes)
        var diff16 = new List<double>();
        for (int i = 16; i < n; i += 16)
        {
            diff16.Add(prices[i] - prices[i - 16]);
        }

        if (diff2.Count < 4 || diff16.Count < 2) return 0.5;

        double mean2 = diff2.Average();
        double var2 = diff2.Sum(d => Math.Pow(d - mean2, 2)) / diff2.Count;
        double std2 = Math.Sqrt(var2);

        double mean16 = diff16.Average();
        double var16 = diff16.Sum(d => Math.Pow(d - mean16, 2)) / diff16.Count;
        double std16 = Math.Sqrt(var16);

        if (std2 < 1e-12) return 0.5;

        // H = log(std16 / std2) / log(16 / 2)
        // since log(16/2) = log(8)
        double ratio = std16 / std2;
        if (ratio < 1e-10) return 0.0;
        
        double hurst = Math.Log(ratio) / Math.Log(8.0);
        return Math.Clamp(hurst, 0.0, 1.0);
    }

    public static double[] ComputeKalmanFilter(double[] prices)
    {
        int n = prices.Length;
        var filtered = new double[n];
        if (n == 0) return filtered;

        // Calculate standard deviation of prices to set R and Q dynamically
        double mean = prices.Average();
        double variance = prices.Sum(p => Math.Pow(p - mean, 2)) / n;
        double std = Math.Sqrt(variance);

        double R = std * std;
        if (R < 1e-10) R = 1e-4;
        double Q = R * 0.02; // Process variance is 2% of measurement noise

        double x = prices[0]; // initial state estimate
        double P = 1.0;       // initial estimation error covariance

        filtered[0] = x;

        for (int i = 1; i < n; i++)
        {
            // Predict
            P = P + Q;

            // Correct
            double K = P / (P + R);
            x = x + K * (prices[i] - x);
            P = (1 - K) * P;

            filtered[i] = x;
        }

        return filtered;
    }

    public static double ComputeDeMarkScore(double[] prices)
    {
        int n = prices.Length;
        if (n < 13) return 0.0;

        int currentBuySetup = 0;
        int currentSellSetup = 0;

        for (int i = 4; i < n; i++)
        {
            if (prices[i] < prices[i - 4])
            {
                currentBuySetup++;
                currentSellSetup = 0;
            }
            else if (prices[i] > prices[i - 4])
            {
                currentSellSetup++;
                currentBuySetup = 0;
            }
            else
            {
                currentBuySetup = 0;
                currentSellSetup = 0;
            }
        }

        // Setup completions (9 through 13 represent mature exhaustion zones)
        if (currentBuySetup >= 9)
        {
            Console.WriteLine($"[TD-Sequential] TD Buy Setup {currentBuySetup} detected (Trend exhausted DOWN -> expecting UP).");
            return 0.35;
        }
        if (currentSellSetup >= 9)
        {
            Console.WriteLine($"[TD-Sequential] TD Sell Setup {currentSellSetup} detected (Trend exhausted UP -> expecting DOWN).");
            return -0.35;
        }

        return 0.0;
    }

    public static (double trendAdj, double rangeAdj, string sessionName) GetSessionMultipliers(bool isForex)
    {
        // Forex sessions only apply on weekdays to Forex pairs
        if (!isForex) return (1.0, 1.0, "CRYPTO / 24/7");

        DayOfWeek day = DateTime.UtcNow.DayOfWeek;
        bool isWeekend = day == DayOfWeek.Saturday || day == DayOfWeek.Sunday;
        if (isWeekend) return (1.0, 1.0, "FOREX WEEKEND (SYNTHETIC)");

        int hour = DateTime.UtcNow.Hour;
        
        // Asian Session (22:00 - 07:00 UTC)
        if (hour >= 22 || hour < 7)
        {
            // Low volatility, range-bound mean reversion is favored
            return (0.75, 1.25, "ASIAN (RANGE)");
        }
        // London/NY Overlap (12:00 - 16:00 UTC)
        else if (hour >= 12 && hour < 16)
        {
            // Maximum volatility and trending breakouts
            return (1.30, 0.70, "LONDON-NY OVERLAP (TREND)");
        }
        // European Session (07:00 - 12:00 UTC)
        else if (hour >= 7 && hour < 12)
        {
            // Trending behavior favored
            return (1.15, 0.85, "LONDON (TREND)");
        }
        // Late US Session (16:00 - 22:00 UTC)
        else
        {
            // Balanced but still trending biased
            return (1.10, 0.90, "NEW YORK (BALANCED)");
        }
    }

    public static double CalculateLrcZscore(double[] prices, int len)
    {
        int n = Math.Min(len, prices.Length);
        if (n < 5) return 0.0;

        var segment = prices.TakeLast(n).ToArray();
        
        // Fit linear regression y = slope * x + intercept
        double sumX = 0, sumY = 0, sumXY = 0, sumX2 = 0;
        for (int i = 0; i < n; i++)
        {
            sumX += i;
            sumY += segment[i];
            sumXY += i * segment[i];
            sumX2 += i * i;
        }
        double denominator = n * sumX2 - sumX * sumX;
        if (Math.Abs(denominator) < 1e-12) return 0.0;

        double slope = (n * sumXY - sumX * sumY) / denominator;
        double intercept = (sumY - slope * sumX) / n;

        // Calculate standard deviation of residuals (distances from the regression line)
        double sumSqResiduals = 0;
        for (int i = 0; i < n; i++)
        {
            double expected = slope * i + intercept;
            double residual = segment[i] - expected;
            sumSqResiduals += residual * residual;
        }
        double stdDev = Math.Sqrt(sumSqResiduals / n);
        if (stdDev < 1e-12) return 0.0;

        // Z-score for the last price
        double lastExpected = slope * (n - 1) + intercept;
        double lastResidual = prices[^1] - lastExpected;

        return lastResidual / stdDev;
    }

}



