using System;
using Xunit;
using Xunit.Abstractions;
using ValutaBot.MiniApp;

namespace ValutaBot.Tests.Engines
{
    public class AutoCalibrationEngineTests
    {
        private readonly ITestOutputHelper _output;

        public AutoCalibrationEngineTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void WeightCalibration_AdjustsBasedOnWinsAndLosses()
        {
            var engine = new AutoCalibrationEngine();

            // TechAnalysis in TrendingImpulse base weight = 1.2
            double initialWeight = engine.GetCalibratedRegimeWeight("TechAnalysis", "EURUSD", "m5", AutoCalibrationEngine.MarketRegime.TrendingImpulse);
            Assert.Equal(1.2, initialWeight, 2);

            for (int i = 0; i < 10; i++) engine.RecordSourceOutcome("TechAnalysis", "EURUSD", "m5", true);
            double winningWeight = engine.GetCalibratedRegimeWeight("TechAnalysis", "EURUSD", "m5", AutoCalibrationEngine.MarketRegime.TrendingImpulse);
            Assert.True(winningWeight > 1.2);

            for (int i = 0; i < 20; i++) engine.RecordSourceOutcome("TechAnalysis", "EURUSD", "m5", false);
            double losingWeight = engine.GetCalibratedRegimeWeight("TechAnalysis", "EURUSD", "m5", AutoCalibrationEngine.MarketRegime.TrendingImpulse);
            Assert.True(losingWeight < 1.2);
        }

        [Fact]
        public void DetectMarketRegime_CorrectlyClassifies()
        {
            var engine = new AutoCalibrationEngine();

            var trending = engine.DetectMarketRegime(40.0, 1.0, 60.0);
            Assert.Equal(AutoCalibrationEngine.MarketRegime.TrendingImpulse, trending);

            var chaotic = engine.DetectMarketRegime(15.0, 2.5, 50.0);
            Assert.Equal(AutoCalibrationEngine.MarketRegime.HighVolatilityChaos, chaotic);

            var ranging = engine.DetectMarketRegime(15.0, 1.0, 50.0);
            Assert.Equal(AutoCalibrationEngine.MarketRegime.RangingFlat, ranging);
        }

        [Fact]
        public void Backtest_AutoCalibration_Vs_StaticWeights()
        {
            _output.WriteLine("--- AUTO CALIBRATION (MODULE 11) BACKTEST RUN ---");
            _output.WriteLine("Simulating 1500 trades through a changing market regime...");
            
            var engine = new AutoCalibrationEngine();
            var rand = new Random(42);

            double dynamicCapital = 1000.0;
            double staticCapital = 1000.0;

            // Base weights used in static simulation
            double staticTaWeight = 1.0;
            double staticOfWeight = 1.0;

            // Scenario: 
            // Phase 1 (Trades 0 to 500): Ranging Market. OrderFlow works great, TA fails often.
            // Phase 2 (Trades 501 to 1500): Trending Market. TA works great, OrderFlow fails often.
            
            for (int i = 0; i < 1500; i++)
            {
                bool isPhase1 = i <= 500;
                
                // Real win probabilities based on market condition
                double realTaWinProb = isPhase1 ? 0.35 : 0.65;
                double realOfWinProb = isPhase1 ? 0.65 : 0.35;

                // Determine if they actually won this trade
                bool taWon = rand.NextDouble() < realTaWinProb;
                bool ofWon = rand.NextDouble() < realOfWinProb;

                // Update auto-calibration engine with outcomes (this happens AFTER trade closes)
                engine.RecordSourceOutcome("TechAnalysis", "EURUSD", "m5", taWon);
                engine.RecordSourceOutcome("OrderFlow", "EURUSD", "m5", ofWon);

                // DYNAMIC CAPITAL UPDATE
                // Our simulated BOT risks capital proportional to the calibrated weight of the strategy.
                var regime = isPhase1 ? AutoCalibrationEngine.MarketRegime.RangingFlat : AutoCalibrationEngine.MarketRegime.TrendingImpulse;
                double dynTaWeight = engine.GetCalibratedRegimeWeight("TechAnalysis", "EURUSD", "m5", regime);
                double dynOfWeight = engine.GetCalibratedRegimeWeight("OrderFlow", "EURUSD", "m5", regime);
                
                // Normalizing weights for allocation
                double totalDynWeight = dynTaWeight + dynOfWeight;
                double allocTaDyn = dynTaWeight / totalDynWeight;
                double allocOfDyn = dynOfWeight / totalDynWeight;

                // Profit/Loss calc for dynamic allocation (Risk 2% per trade, EV = 1.5R)
                double risk = 20.0; // Fixed risk for simplicity
                double pnlTaDyn = (taWon ? risk * 1.5 : -risk) * allocTaDyn;
                double pnlOfDyn = (ofWon ? risk * 1.5 : -risk) * allocOfDyn;
                dynamicCapital += pnlTaDyn + pnlOfDyn;

                // STATIC CAPITAL UPDATE
                // Normalizing static weights
                double totalStaticWeight = staticTaWeight + staticOfWeight;
                double allocTaStatic = staticTaWeight / totalStaticWeight;
                double allocOfStatic = staticOfWeight / totalStaticWeight;

                double pnlTaStatic = (taWon ? risk * 1.5 : -risk) * allocTaStatic;
                double pnlOfStatic = (ofWon ? risk * 1.5 : -risk) * allocOfStatic;
                staticCapital += pnlTaStatic + pnlOfStatic;
            }

            _output.WriteLine("Total Trades: 1500");
            _output.WriteLine("Market Regime shifted at Trade 500.");
            _output.WriteLine("-------------------------------------------");
            _output.WriteLine("Starting Capital: $1000.00");
            _output.WriteLine("Static Weights Final Capital: $" + Math.Round(staticCapital, 2));
            _output.WriteLine("Dynamic Calibration Final Capital: $" + Math.Round(dynamicCapital, 2));
            
            double staticRoi = ((staticCapital - 1000.0) / 1000.0) * 100;
            double dynamicRoi = ((dynamicCapital - 1000.0) / 1000.0) * 100;
            
            _output.WriteLine("Static Weights ROI: " + Math.Round(staticRoi, 1) + "%");
            _output.WriteLine("Dynamic Calibration ROI: " + Math.Round(dynamicRoi, 1) + "%");
            _output.WriteLine("-------------------------------------------");
            
            var regimeFinal = AutoCalibrationEngine.MarketRegime.TrendingImpulse;
            double finalTaWeight = engine.GetCalibratedRegimeWeight("TechAnalysis", "EURUSD", "m5", regimeFinal);
            double finalOfWeight = engine.GetCalibratedRegimeWeight("OrderFlow", "EURUSD", "m5", regimeFinal);
            
            _output.WriteLine("Final Dynamic TechAnalysis Weight: " + Math.Round(finalTaWeight, 2));
            _output.WriteLine("Final Dynamic OrderFlow Weight: " + Math.Round(finalOfWeight, 2));

            Assert.True(dynamicCapital > staticCapital, "Dynamic auto-calibration should outperform static weights");
        }
    }
}
