using System;

namespace ValutaBot.MiniApp;

public record MonteCarloResult(
    int Iterations,
    int SuccessCount,
    double ExpectedValuePct,
    double KellyRiskPct,
    string EvLabel,
    string KellyLabel,
    string SummaryReasoning
);

public static class MonteCarloEngine
{
    /// <summary>
    /// Runs 1,000 algorithmic Monte Carlo stochastic price path simulations with ATR volatility and calculates
    /// Expected Value (EV) and Fractional Kelly Criterion risk management.
    /// </summary>
    public static MonteCarloResult Simulate(
        double currentPrice,
        double winProbability,
        string direction,
        double atr,
        int timeInSeconds = 60,
        double payoutRatio = 0.85,
        int iterations = 1000)
    {
        if (currentPrice <= 0) currentPrice = 1.0;
        if (atr <= 0) atr = currentPrice * 0.0005; // Fallback volatility 0.05%
        
        double prob = Math.Clamp(winProbability, 0.50, 0.95);
        bool isBuy = direction.Equals("BUY", StringComparison.OrdinalIgnoreCase);

        // Normalize volatility per second
        double volPerSec = (atr / currentPrice) / Math.Sqrt(60.0);
        double totalTimeStep = Math.Max(10, timeInSeconds);
        double totalVol = volPerSec * Math.Sqrt(totalTimeStep);

        // Directional drift based on probability
        double driftSign = isBuy ? 1.0 : -1.0;
        double directionalDrift = (prob - 0.5) * 2.0 * totalVol;

        int successCount = 0;
        var rand = Random.Shared;

        // 1,000 Stochastic Monte Carlo iterations
        for (int i = 0; i < iterations; i++)
        {
            // Box-Muller transformation for standard normal Gaussian random numbers
            double u1 = 1.0 - rand.NextDouble();
            double u2 = 1.0 - rand.NextDouble();
            double randNormal = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);

            // Geometric Brownian Motion step
            double simulatedReturn = directionalDrift + (totalVol * randNormal);
            double finalSimulatedPrice = currentPrice * Math.Exp(simulatedReturn);

            if (isBuy && finalSimulatedPrice > currentPrice)
            {
                successCount++;
            }
            else if (!isBuy && finalSimulatedPrice < currentPrice)
            {
                successCount++;
            }
        }

        double simulatedWinRate = (double)successCount / iterations;

        // Calculate Expected Value (EV): EV = (Win% * Payout) - (Loss% * 1.0)
        double evRatio = (simulatedWinRate * payoutRatio) - ((1.0 - simulatedWinRate) * 1.0);
        double evPct = Math.Round(evRatio * 100.0, 1);

        // Calculate Kelly Criterion Risk Percentage: K% = (p * b - q) / b
        double p = simulatedWinRate;
        double q = 1.0 - p;
        double b = payoutRatio > 0 ? payoutRatio : 0.85;

        double fullKelly = (p * b - q) / b;
        // Fractional Kelly (Half-Kelly to Fractional 25% for conservative capital preservation)
        double fractionalKelly = Math.Clamp(fullKelly * 0.25, 0.01, 0.05);
        double kellyRiskPct = Math.Round(fractionalKelly * 100.0, 1);

        string evLabel = evPct > 0 
            ? $"+{evPct:F1}% EV (Высокая выгода)" 
            : $"{evPct:F1}% EV (Низкое матожидание)";

        string kellyLabel = $"{kellyRiskPct:F1}% - {Math.Min(kellyRiskPct + 0.5, 5.0):F1}% от депозита";

        string summary = $"🎰 Монте-Карло (1000 прогонов ATR): {successCount}/{iterations} успехов | EV: {(evPct > 0 ? "+" : "")}{evPct:F1}% | Риск Келли: {kellyRiskPct:F1}%";

        return new MonteCarloResult(
            iterations,
            successCount,
            evPct,
            kellyRiskPct,
            evLabel,
            kellyLabel,
            summary
        );
    }
}
