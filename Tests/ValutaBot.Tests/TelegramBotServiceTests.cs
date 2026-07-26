// TelegramBotServiceTests.cs - basic unit tests for TelegramBotService
using Xunit;
using ValutaBot.MiniApp;

namespace ValutaBot.Tests
{
    public class TelegramBotServiceTests
    {
        [Fact]
        public void IsUserAllowed_ReturnsFalse_ForUnknownUser()
        {
            // Use a very unlikely chat ID that is guaranteed not to be in the allowed list
            bool result = TelegramBotService.IsUserAllowed(long.MinValue);
            Assert.False(result);
        }
    }
}
