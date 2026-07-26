// CoreTests.cs - unit tests for critical static components
using Xunit;
using ValutaBot.MiniApp;

namespace ValutaBot.Tests
{
    public class CoreTests
    {
        [Fact]
        public void TelegramBotService_IsUserAllowed_ReturnsFalseForUnknownId()
        {
            // TelegramBotService.IsUserAllowed is a static method
            bool result = TelegramBotService.IsUserAllowed(-999);
            Assert.False(result);
        }

        [Fact]
        public void DailyRiskCircuitBreaker_EvaluateRiskStatus_ReturnsResult()
        {
            // DailyRiskCircuitBreaker is a static class
            var status = DailyRiskCircuitBreaker.EvaluateRiskStatus(12345);
            Assert.NotNull(status);
        }

        [Fact]
        public void AutoCalibrationEngine_DetectMarketRegime_ClassifiesCorrectly()
        {
            // High ADX + low volRatio → TrendingImpulse
            var regime = AutoCalibrationEngine.DetectMarketRegime(adx: 30.0, volRatio: 1.5, rsi: 60.0);
            Assert.Equal(AutoCalibrationEngine.MarketRegime.TrendingImpulse, regime);

            // Low ADX + low volRatio + neutral RSI → RangingFlat
            var regime2 = AutoCalibrationEngine.DetectMarketRegime(adx: 15.0, volRatio: 1.0, rsi: 50.0);
            Assert.Equal(AutoCalibrationEngine.MarketRegime.RangingFlat, regime2);
        }
    }
}
