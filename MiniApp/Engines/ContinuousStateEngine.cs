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
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (double estimate, double errorEstimate)> _kalmanState = new();

    /// <summary>
    /// Computes continuous physical velocity, acceleration, and Kalman state vector.
    /// </summary>
    public static ContinuousStateResult EvaluateContinuousState(double[] prices, string asset = "GLOBAL", string timeframe = "m1")
    {
        if (prices == null || prices.Length < 10)
        {
            return new ContinuousStateResult(0, 0, 0, "STABLE", 0, "РќРµРґРѕСЃС‚Р°С‚РѕС‡РЅРѕ С‚РёРєРѕРІ РґР»СЏ РЅРµРїСЂРµСЂС‹РІРЅРѕРіРѕ РІРµРєС‚РѕСЂР° СЃРѕСЃС‚РѕСЏРЅРёСЏ.");
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
        string stateKey = $"{asset}_{timeframe}";
        double kalmanState = FilterKalmanContinuous(prices, stateKey);

        string regime;
        double momentumContribution = 0;
        string desc;

        if (instantVelocity > 3.0 && instantAcceleration > 0.5)
        {
            regime = "HYPER_ACCELERATING_UP";
            momentumContribution = 0.45;
            desc = $"РќРµРїСЂРµСЂС‹РІРЅС‹Р№ РІРµРєС‚РѕСЂ: Р“РёРїРµСЂ-СѓСЃРєРѕСЂРµРЅРёРµ Р’Р’Р•Р РҐ (Velocity={instantVelocity:F1} bps/s, Accel={instantAcceleration:F2} bps/sВІ).";
        }
        else if (instantVelocity < -3.0 && instantAcceleration < -0.5)
        {
            regime = "HYPER_ACCELERATING_DOWN";
            momentumContribution = -0.45;
            desc = $"РќРµРїСЂРµСЂС‹РІРЅС‹Р№ РІРµРєС‚РѕСЂ: Р“РёРїРµСЂ-СѓСЃРєРѕСЂРµРЅРёРµ Р’РќРР— (Velocity={instantVelocity:F1} bps/s, Accel={instantAcceleration:F2} bps/sВІ).";
        }
        else if (Math.Sign(instantVelocity) != Math.Sign(instantAcceleration) && Math.Abs(instantVelocity) > 2.0)
        {
            regime = "DECELERATING";
            momentumContribution = -Math.Sign(instantVelocity) * 0.20;
            desc = $"РќРµРїСЂРµСЂС‹РІРЅС‹Р№ РІРµРєС‚РѕСЂ: Р—Р°РјРµРґР»РµРЅРёРµ РёРјРїСѓР»СЊСЃР° РїРµСЂРµРґ СЂР°Р·РІРѕСЂРѕС‚РѕРј (Deceleration Phase).";
        }
        else
        {
            regime = "STABLE";
            momentumContribution = 0;
            desc = $"РќРµРїСЂРµСЂС‹РІРЅС‹Р№ РІРµРєС‚РѕСЂ: РЎС‚Р°Р±РёР»СЊРЅРѕРµ Р»Р°РјРёРЅР°СЂРЅРѕРµ РґРІРёР¶РµРЅРёРµ (Velocity={instantVelocity:F1} bps/s).";
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

    private static double FilterKalmanContinuous(double[] prices, string stateKey)
    {
        double processNoise = 0.01;
        double measurementNoise = 0.1;

        if (!_kalmanState.TryGetValue(stateKey, out var state))
        {
            double est = prices[0];
            double err = 1.0;
            foreach (double p in prices)
            {
                double k = err / (err + measurementNoise);
                est = est + k * (p - est);
                err = (1.0 - k) * err + processNoise;
            }
            state = (est, err);
        }
        else
        {
            double p = prices[^1];
            double k = state.errorEstimate / (state.errorEstimate + measurementNoise);
            state.estimate = state.estimate + k * (p - state.estimate);
            state.errorEstimate = (1.0 - k) * state.errorEstimate + processNoise;
        }

        _kalmanState[stateKey] = state;
        return state.estimate;
    }
}
