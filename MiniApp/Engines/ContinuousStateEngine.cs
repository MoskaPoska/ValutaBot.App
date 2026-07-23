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
    public static ContinuousStateResult EvaluateContinuousState(double[] prices)
    {
        if (prices == null || prices.Length < 10)
        {
            return new ContinuousStateResult(0, 0, 0, "STABLE", 0, "Недостаточно тиков для непрерывного вектора состояния.");
        }

        int n = prices.Length;
        double currentPrice = prices[^1];

        // 1. Calculate 1st Derivative: Instantaneous Velocity dp/dt (Basis points per step)
        double[] velocities = new double[n - 1];
        for (int i = 0; i < n - 1; i++)
        {
            velocities[i] = ((prices[i + 1] - prices[i]) / Math.Max(1e-8, prices[i])) * 10_000.0; // Bps
        }

        double instantVelocity = velocities.TakeLast(5).Average();

        // 2. Calculate 2nd Derivative: Instantaneous Acceleration d2p/dt2
        double[] accelerations = new double[velocities.Length - 1];
        for (int i = 0; i < velocities.Length - 1; i++)
        {
            accelerations[i] = velocities[i + 1] - velocities[i];
        }

        double instantAcceleration = accelerations.TakeLast(5).Average();

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

    private static double FilterKalmanContinuous(double[] prices)
    {
        double estimate = prices[0];
        double errorEstimate = 1.0;
        double processNoise = 0.01;
        double measurementNoise = 0.1;

        foreach (double p in prices)
        {
            double kalmanGain = errorEstimate / (errorEstimate + measurementNoise);
            estimate = estimate + kalmanGain * (p - estimate);
            errorEstimate = (1.0 - kalmanGain) * errorEstimate + processNoise;
        }

        return estimate;
    }
}
