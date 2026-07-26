// TechnicalAnalysisEngineTests.cs - basic unit tests for TechnicalAnalysisEngine
using Xunit;
using ValutaBot.MiniApp;

namespace ValutaBot.Tests
{
    public class TechnicalAnalysisEngineTests
    {
        [Fact]
        public void ComputeRsi_WithValidPrices_ReturnsValueInRange()
        {
            // TechnicalAnalysisEngine is a static class with static methods
            double[] prices = { 100.0, 101.0, 99.5, 102.0, 101.5, 103.0, 104.0, 102.5, 105.0, 106.0, 104.5, 107.0, 108.0, 106.5, 109.0 };
            double rsi = TechnicalAnalysisEngine.ComputeRsi(prices, 14);
            Assert.InRange(rsi, 0, 100);
        }
    }
}
