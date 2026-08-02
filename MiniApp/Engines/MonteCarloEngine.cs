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
        
        double prob = Math.Clamp(winProbability, 0.35, 0.95);
        bool isBuy = direction.Equals("BUY", StringComparison.OrdinalIgnoreCase);

        // Normalize volatility per second
        double volPerSec = (atr / currentPrice) / Math.Sqrt(60.0);
        double totalTimeStep = Math.Max(10, timeInSeconds);
        double totalVol = volPerSec * Math.Sqrt(totalTimeStep);

        // Ito's drift correction for Geometric Brownian Motion
        double itoDrift = -0.5 * totalVol * totalVol;

        // Directional drift based on probability
        double driftSign = isBuy ? 1.0 : -1.0;
        double directionalDrift = (driftSign * (prob - 0.5) * 2.0 * totalVol) + itoDrift;

        // Merton Jump Diffusion Parameters (Fat Tails)
        // Set a capped expected jumps so higher timeframes don't get mathematically crushed.
        double expectedJumps = 0.15; // 15% chance of a black swan jump per simulation, regardless of timeframe
        double jumpVol = totalVol * 3.0; // Jumps are 3x more volatile than normal Brownian motion

        int successCount = 0;
        var rand = Random.Shared;

        // 1,000 Stochastic Monte Carlo iterations
        for (int i = 0; i < iterations; i++)
        {
            // Box-Muller transform for standard normal Gaussian random numbers
            double u1 = 1.0 - rand.NextDouble();
            double u2 = 1.0 - rand.NextDouble();
            double randNormal = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);

            // Simulate Poisson Jumps (Black Swan / Manipulation)
            double jumpReturn = 0;
            if (rand.NextDouble() < expectedJumps) // simplified Poisson trigger for small intervals
            {
                double j1 = 1.0 - rand.NextDouble();
                double j2 = 1.0 - rand.NextDouble();
                double jumpNormal = Math.Sqrt(-2.0 * Math.Log(j1)) * Math.Sin(2.0 * Math.PI * j2);
                
                // Jumps usually happen against the obvious crowd direction in crypto (liquidation hunting)
                double jumpMean = -driftSign * totalVol * 1.5; 
                jumpReturn = jumpMean + (jumpNormal * jumpVol);
            }

            // Merton Jump-Diffusion Geometric Brownian Motion step
            double simulatedReturn = directionalDrift + (totalVol * randNormal) + jumpReturn;
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
        double fractionalKelly = Math.Clamp(fullKelly * 0.25, 0.0, 0.05);
        double kellyRiskPct = Math.Round(fractionalKelly * 100.0, 1);

        string evLabel = evPct > 0 
            ? $"+{evPct:F1}% EV (Высокая выгода)" 
            : $"{evPct:F1}% EV (Низкое матожидание)";

        string kellyLabel = kellyRiskPct > 0 
            ? $"{kellyRiskPct:F1}% - {Math.Min(kellyRiskPct + 0.5, 5.0):F1}% от депозита"
            : "0% (Не рекомендуется открывать сделку)";

        string summary = $"🎰 Монте-Карло ({iterations} прогонов ATR): {successCount}/{iterations} успехов | EV: {(evPct > 0 ? "+" : "")}{evPct:F1}% | Риск Келли: {kellyRiskPct:F1}%";

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
