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
        public void LightGbmEngine_ShouldTrainAndPredict()
        {
            // Arrange
            var mlEngine = new LightGbmEngine();
            
            var histData = new List<TradeFeatureData>
            {
                new TradeFeatureData { Open=10, High=12, Low=9, Close=11, Volume=1000, Rsi=40, ClusterDelta=50, IsUp=true },
                new TradeFeatureData { Open=11, High=13, Low=10, Close=12, Volume=1100, Rsi=45, ClusterDelta=60, IsUp=true },
                new TradeFeatureData { Open=12, High=12.5f, Low=8, Close=9, Volume=2000, Rsi=70, ClusterDelta=-100, IsUp=false },
                new TradeFeatureData { Open=9, High=10, Low=7, Close=8, Volume=1500, Rsi=80, ClusterDelta=-150, IsUp=false },
                new TradeFeatureData { Open=8, High=9, Low=7.5f, Close=8.5f, Volume=800, Rsi=30, ClusterDelta=10, IsUp=true }
            };

            // Act
            mlEngine.TrainModel(histData);
            
            var testData = new TradeFeatureData { Open=8.5f, High=10, Low=8, Close=9.5f, Volume=1200, Rsi=35, ClusterDelta=30 };
            var prediction = mlEngine.Predict(testData);
            var (prob, rec) = mlEngine.AnalyzeProbability(prediction);

            // Assert
            Assert.NotNull(prediction);
            Assert.True(prob >= 0f && prob <= 1f, "Probability should be bounded between 0 and 1");
            Assert.NotNull(rec);
            _output.WriteLine($"[LightGBM Test] Probability: {prob:P2}, Recommendation: {rec}");
        }

        [Fact]
        public void LlmReportingService_ShouldGenerateAccurateText()
        {
            // Arrange
            var llmService = new LlmReportingService();
            
            // Act
            string report = llmService.GenerateMarketSummary(
                asset: "BTCUSDT", 
                regime: "Trend (Bullish)", 
                lightGbmProb: 0.85f, 
                l1IsBuy: true, 
                l2IsBuy: true, 
                l3IsBuy: false
            );

            // Assert
            Assert.NotNull(report);
            Assert.Contains("BTCUSDT", report);
            Assert.Contains("85%", report);
            Assert.Contains("2/3", report);
            Assert.Contains("ЛОНГ", report);
            _output.WriteLine($"[LLM Test] Output:\n{report}");
        }
    }
}
