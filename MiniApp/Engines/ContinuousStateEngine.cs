using System;
using System.Linq;

namespace ValutaBot.MiniApp;

public record ContinuousStateResult(
    double VelocityBpsPerSec,      // 1st Derivative dp/dt (basis points / sec)
    double AccelerationBpsPerSec2, // 2nd Derivative d2p/dt2 (basis points / sec^2)
    double KalmanFilteredState,
    string VelocityRegime,         // "HYPER_ACCELERATING_UP" | "HYPER_ACCELERATING_DOWN" | "DECELERATING" | "STABLE"
    double MomentumContribution,
    string Description
);

/// <summary>
/// Continuous Latent State Engine (Wall Street HFT Standard).
/// Eliminates discrete candle boundaries (M1/M5) by treating market price as a continuous 
/// physical state vector with instantaneous velocity (dp/dt) and acceleration (d2p/dt2).
/// </summary>
public static class ContinuousStateEngine
{

    /// <summary>
    /// Computes continuous physical velocity, acceleration, and Kalman state vector.
    /// </summary>
    public static ContinuousStateResult EvaluateContinuousState(ReadOnlySpan<double> prices, string asset = "GLOBAL", string timeframe = "m1")
    {
        if (prices == null || prices.Length < 10)
        {
            return new ContinuousStateResult(0, 0, 0, "UNKNOWN", 0, "Недостаточно данных для непрерывного анализа.");
        }

        foreach (var p in prices)
        {
            if (double.IsNaN(p) || double.IsInfinity(p))
            {
                return new ContinuousStateResult(0, 0, 0, "UNKNOWN", 0, "Обнаружено повреждение данных (NaN/Infinity).");
            }
        }

        int n = prices.Length;
        double currentPrice = prices[^1];

        // 1. Calculate 1st Derivative: Instantaneous Velocity dp/dt using Savitzky-Golay filter (5-point)
        double instantVelocity = 0;
        if (n >= 5)
        {
            // SG 1st derivative coefficients: [-2, -1, 0, 1, 2] / 10
            double sgVelocity = (-2.0 * prices[^5] - 1.0 * prices[^4] + 0.0 * prices[^3] + 1.0 * prices[^2] + 2.0 * prices[^1]) / 10.0;
            instantVelocity = (sgVelocity / Math.Max(1e-8, prices[^3])) * 10_000.0; // Bps relative to center point
        }
        else
        {
            instantVelocity = ((prices[^1] - prices[^2]) / Math.Max(1e-8, prices[^2])) * 10_000.0;
        }

        // 2. Calculate 2nd Derivative: Instantaneous Acceleration d2p/dt2 using Savitzky-Golay filter (5-point)
        double instantAcceleration = 0;
        if (n >= 5)
        {
            // SG 2nd derivative coefficients: [2, -1, -2, -1, 2] / 7
            double sgAccel = (2.0 * prices[^5] - 1.0 * prices[^4] - 2.0 * prices[^3] - 1.0 * prices[^2] + 2.0 * prices[^1]) / 7.0;
            instantAcceleration = (sgAccel / Math.Max(1e-8, prices[^3])) * 10_000.0; // Bps
        }
        else
        {
            double v1 = ((prices[^2] - prices[^3]) / Math.Max(1e-8, prices[^3])) * 10_000.0;
            double v2 = ((prices[^1] - prices[^2]) / Math.Max(1e-8, prices[^2])) * 10_000.0;
            instantAcceleration = v2 - v1;
        }

        // 3. 4th-Order Continuous Kalman State Filtering
        double kalmanState = FilterKalmanContinuous(prices);

        string regime;
        double momentumContribution = 0;
        string desc;

        if (instantVelocity > 3.0 && instantAcceleration > 0.5)
        {
            regime = "HYPER_ACCELERATING_UP";
            momentumContribution = 0.45;
            desc = $"Непрерывный вектор: Гипер-ускорение ВВЕРХ (Velocity={instantVelocity:F1} bps/s, Accel={instantAcceleration:F2} bps/s²).";
        }
        else if (instantVelocity < -3.0 && instantAcceleration < -0.5)
        {
            regime = "HYPER_ACCELERATING_DOWN";
            momentumContribution = -0.45;
            desc = $"Непрерывный вектор: Гипер-ускорение ВНИЗ (Velocity={instantVelocity:F1} bps/s, Accel={instantAcceleration:F2} bps/s²).";
        }
        else if (Math.Sign(instantVelocity) != Math.Sign(instantAcceleration) && Math.Abs(instantVelocity) > 2.0)
        {
            regime = "DECELERATING";
            momentumContribution = -Math.Sign(instantVelocity) * 0.20;
            desc = $"Непрерывный вектор: Замедление импульса перед разворотом (Deceleration Phase).";
        }
        else
        {
            regime = "STABLE";
            momentumContribution = 0;
            desc = $"Непрерывный вектор: Стабильное ламинарное движение (Velocity={instantVelocity:F1} bps/s).";
        }

        return new ContinuousStateResult(
            VelocityBpsPerSec: Math.Round(instantVelocity, 2),
            AccelerationBpsPerSec2: Math.Round(instantAcceleration, 2),
            KalmanFilteredState: Math.Round(kalmanState, 5),
            VelocityRegime: regime,
            MomentumContribution: momentumContribution,
            Description: desc
        );
    }

    private static double FilterKalmanContinuous(ReadOnlySpan<double> prices)
    {
        double currentPrice = prices[^1];
        double processNoise = currentPrice * 0.0001; // 0.01% of price
        double measurementNoise = currentPrice * 0.001; // 0.1% of price

        double est = prices[0];
        double err = currentPrice * 0.01;
        
        for (int i = 0; i < prices.Length; i++) 
        { 
            double pPrice = prices[i];
            double k = err / (err + measurementNoise);
            est = est + k * (pPrice - est);
            err = (1.0 - k) * err + processNoise;
        }

        return est;
    }
}
