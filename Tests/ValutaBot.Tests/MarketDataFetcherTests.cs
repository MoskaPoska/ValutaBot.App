// MarketDataFetcherTests.cs - unit tests for MarketDataFetcher utility methods
using Xunit;
using ValutaBot.MiniApp;

namespace ValutaBot.Tests
{
    public class MarketDataFetcherTests
    {
        [Fact]
        public void IntervalMap_ReturnsCorrectMapping()
        {
            Assert.Equal("1m", MarketDataFetcher.IntervalMap("s5"));
            Assert.Equal("5m", MarketDataFetcher.IntervalMap("m5"));
            Assert.Equal("1h", MarketDataFetcher.IntervalMap("h1"));
        }

        [Fact]
        public void TimeframeSeconds_ReturnsCorrectSeconds()
        {
            Assert.Equal(60, MarketDataFetcher.TimeframeSeconds("m1"));
            Assert.Equal(300, MarketDataFetcher.TimeframeSeconds("m5"));
            Assert.Equal(3600, MarketDataFetcher.TimeframeSeconds("h1"));
        }
    }
}
