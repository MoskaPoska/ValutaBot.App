using System;
using Xunit;
using Xunit.Abstractions;
using ValutaBot.MiniApp;

namespace ValutaBot.Tests.Engines
{
    public class MonteCarloEngineTests
    {
        private readonly IMonteCarloEngine _engine;
        private readonly ITestOutputHelper _output;

        public MonteCarloEngineTests(ITestOutputHelper output)
        {
            _engine = new MonteCarloEngine();
            _output = output;
        }

        [Fact]
        public void Simulate_BuyWithHighProbability_ShouldHavePositiveEV()
        {
            double currentPrice = 50000;
            double winProb = 0.90; 
            string direction = "BUY";
            double atr = 250; 
            var result = _engine.Simulate(currentPrice, winProb, direction, atr, 300, 0.85, 1000);
            Assert.Equal(1000, result.Iterations);
            Assert.True(result.ExpectedValuePct > 0);
            Assert.True(result.KellyRiskPct > 0);
        }

        [Fact]
        public void Simulate_BuyWithLowProbability_ShouldHaveNegativeEV()
        {
            var result = _engine.Simulate(50000, 0.40, "BUY", 250, 300, 0.85, 1000);
            Assert.True(result.ExpectedValuePct < 0);
            Assert.Equal(0, result.KellyRiskPct); 
        }

        [Fact]
        public void Simulate_FallbackValues_ShouldNotCrash()
        {
            var result = _engine.Simulate(0, 0, "UNKNOWN", 0, 0, 0, 100);
            Assert.NotNull(result);
        }

        [Fact]
        public void Backtest_MonteCarloDynamicKelly_Vs_FixedRisk()
        {
            _output.WriteLine("--- MONTE CARLO (MODULE 9) BACKTEST RUN ---");
            _output.WriteLine("Simulating 1000 sequential trades with dynamic Kelly Criterion vs Fixed 1% Risk...");

            var rand = new Random(42); 
            double startingCapital = 1000.0;
            double kellyCapital = startingCapital;
            double fixedCapital = startingCapital;
            int wins = 0;
            int losses = 0;
            double payoutRatio = 0.85;

            for (int i = 0; i < 1000; i++)
            {
                double trueProb = 0.50 + (rand.NextDouble() * 0.15); 
                var mcResult = _engine.Simulate(100.0, trueProb, "BUY", 0.5, 60, payoutRatio, 500);

                if (mcResult.ExpectedValuePct <= 0 || mcResult.KellyRiskPct <= 0)
                {
                    continue; 
                }

                bool isWin = rand.NextDouble() < trueProb;
                double kellyBet = kellyCapital * (mcResult.KellyRiskPct / 100.0);
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

                if (kellyCapital <= 0) kellyCapital = 0;
                if (fixedCapital <= 0) fixedCapital = 0;
            }

            _output.WriteLine("Trades Executed: " + (wins + losses));
            _output.WriteLine("Wins: " + wins + " | Losses: " + losses + " | WinRate: " + Math.Round((double)wins/(wins+losses)*100, 1) + "%");
            _output.WriteLine("-------------------------------------------");
            _output.WriteLine("Starting Capital: " + startingCapital.ToString("C"));
            _output.WriteLine("Fixed 1% Risk Final Capital: " + fixedCapital.ToString("C"));
            _output.WriteLine("Dynamic Kelly Final Capital: " + kellyCapital.ToString("C"));
            
            double fixedRoi = ((fixedCapital - startingCapital) / startingCapital) * 100;
            double kellyRoi = ((kellyCapital - startingCapital) / startingCapital) * 100;
            
            _output.WriteLine("Fixed Risk ROI: " + Math.Round(fixedRoi, 1) + "%");
            _output.WriteLine("Kelly Risk ROI: " + Math.Round(kellyRoi, 1) + "%");
            
            Assert.True(kellyCapital > fixedCapital, "Kelly Criterion should outperform Fixed Risk in a positive edge environment");
        }
    }
}
