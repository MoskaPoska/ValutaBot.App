// BotDatabaseTests.cs - basic unit tests for BotDatabase
using Xunit;
using ValutaBot.MiniApp;

namespace ValutaBot.Tests
{
    public class BotDatabaseTests
    {
        [Fact]
        public void Initialize_DoesNotThrow()
        {
            // BotDatabase is a static class; Initialize sets up SQLite
            var ex = Record.Exception(() => BotDatabase.Initialize());
            Assert.Null(ex);
        }
    }
}
