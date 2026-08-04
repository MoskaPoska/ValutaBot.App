using System;
using ValutaBot.MiniApp;

public class MonteCarloBacktester
{
    public static void Main()
    {
        Console.WriteLine("--- MONTE CARLO (MODULE 9) BACKTEST RUN ---");
        Console.WriteLine("Simulating 1000 sequential trades with dynamic Kelly Criterion vs Fixed 1% Risk...");

        var engine = new MonteCarloEngine();
        var rand = new Random(42); // fixed seed for reproducibility

        double startingCapital = 1000.0;
        
        double kellyCapital = startingCapital;
        double fixedCapital = startingCapital;

        int wins = 0;
        int losses = 0;
        double payoutRatio = 0.85;

        for (int i = 0; i < 1000; i++)
        {
            // Simulate a trade setup where our system detects an edge.
            // Let's assume our actual win probability fluctuates between 50% and 65% for valid setups.
            double trueProb = 0.50 + (rand.NextDouble() * 0.15); 
            
            // The engine calculates the optimal Kelly bet size
            var mcResult = engine.Simulate(100.0, trueProb, "BUY", 0.5, 60, payoutRatio, 500);

            // Our bot only takes trades with EV > 0 and Kelly > 0
            if (mcResult.ExpectedValuePct <= 0 || mcResult.KellyRiskPct <= 0)
            {
                continue; // Skip bad setups
            }

            // Decide outcome based on the true probability
            bool isWin = rand.NextDouble() < trueProb;

            // 1. Calculate Kelly Bet
            double kellyBet = kellyCapital * (mcResult.KellyRiskPct / 100.0);
            
            // 2. Calculate Fixed Bet (1%)
            double fixedBet = fixedCapital * 0.01;

            if (isWin)
            {
                wins++;
                kellyCapital += kellyBet * payoutRatio;
                fixedCapital += fixedBet * payoutRatio;
            }
            else
            {
                losses++;
                kellyCapital -= kellyBet;
                fixedCapital -= fixedBet;
            }

            // Margin call check
            if (kellyCapital <= 0) kellyCapital = 0;
            if (fixedCapital <= 0) fixedCapital = 0;
        }

        Console.WriteLine("Trades Executed: " + (wins + losses));
        Console.WriteLine("Wins: " + wins + " | Losses: " + losses + " | WinRate: " + Math.Round((double)wins/(wins+losses)*100, 1) + "%");
        Console.WriteLine("-------------------------------------------");
        Console.WriteLine("Starting Capital: " + startingCapital.ToString("C"));
        Console.WriteLine("Fixed 1% Risk Final Capital: " + fixedCapital.ToString("C"));
        Console.WriteLine("Dynamic Kelly Final Capital: " + kellyCapital.ToString("C"));
        
        double fixedRoi = ((fixedCapital - startingCapital) / startingCapital) * 100;
        double kellyRoi = ((kellyCapital - startingCapital) / startingCapital) * 100;
        
        Console.WriteLine("Fixed Risk ROI: " + Math.Round(fixedRoi, 1) + "%");
        Console.WriteLine("Kelly Risk ROI: " + Math.Round(kellyRoi, 1) + "%");
        
        if (kellyCapital > fixedCapital)
        {
            Console.WriteLine("CONCLUSION: Monte Carlo Fractional Kelly outperforms Fixed Risk.");
        }
    }
}
