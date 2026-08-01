using System;
using System.Collections.Generic;
using Xunit;
using Xunit.Abstractions;
using ValutaBot.App.MiniApp.Engines.ML;
using ValutaBot.App.MiniApp.Services;

namespace ValutaBot.Tests
{
    public class MlModuleTests
    {
        private readonly ITestOutputHelper _output;

        public MlModuleTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void EnsembleMlEngine_ShouldTrainAndPredict()
        {
            // Arrange
            var mlEngine = new EnsembleMlEngine();
            
            var histData = new List<TradeFeatureData>
            {
                new TradeFeatureData { Open=10, High=12, Low=9, Close=11, Volume=1000, Rsi=40, ClusterDelta=50, IsUp=true },
                new TradeFeatureData { Open=11, High=13, Low=10, Close=12, Volume=1100, Rsi=45, ClusterDelta=60, IsUp=true },
                new TradeFeatureData { Open=12, High=12.5f, Low=8, Close=9, Volume=2000, Rsi=70, ClusterDelta=-100, IsUp=false },
                new TradeFeatureData { Open=9, High=10, Low=7, Close=8, Volume=1500, Rsi=80, ClusterDelta=-150, IsUp=false },
                new TradeFeatureData { Open=8, High=9, Low=7.5f, Close=8.5f, Volume=800, Rsi=30, ClusterDelta=10, IsUp=true }
            };

            // Act
            mlEngine.TrainModels(histData);
            
            var testData = new TradeFeatureData { Open=8.5f, High=10, Low=8, Close=9.5f, Volume=1200, Rsi=35, ClusterDelta=30 };
            var prediction = mlEngine.PredictEnsemble(testData);

            // Assert
            Assert.NotNull(prediction);
            Assert.True(prediction.AverageProbability >= 0f && prediction.AverageProbability <= 1f, "Probability should be bounded between 0 and 1");
            _output.WriteLine($"[Ensemble Test] Probability: {prediction.AverageProbability:P2}, Consensus: {prediction.ConsensusPrediction}");
        }

        [Fact]
        public void LlmReportingService_ShouldGenerateAccurateText()
        {
            // Arrange
            var llmService = new LlmReportingService();
            var dummyPrediction = new EnsemblePrediction 
            {
                ConsensusPrediction = true,
                AverageProbability = 0.85f,
            };
            dummyPrediction.ModelProbabilities["LightGBM"] = 0.90f;
            dummyPrediction.ModelProbabilities["FastTree"] = 0.85f;
            dummyPrediction.ModelProbabilities["FastForest"] = 0.80f;

            // Act
            string report = llmService.GenerateMarketSummary(
                asset: "BTCUSDT", 
                regime: "Uptrend", 
                mlPrediction: dummyPrediction, 
                l1IsBuy: true, 
                l2IsBuy: true, 
                l3IsBuy: true);

            // Assert
            Assert.Contains("BTCUSDT", report);
            Assert.Contains("90%", report);
            Assert.Contains("ВВЕРХ", report);
            Assert.Contains("3/3", report);
            _output.WriteLine(report);
        }
    }
}
