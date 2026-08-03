#if false
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Moq.Protected;
using Xunit;
using Xunit.Abstractions;
using ValutaBot.MiniApp;

namespace ValutaBot.Tests
{
    public class ChaosEngineeringTests
    {
        private readonly ITestOutputHelper _output;

        public ChaosEngineeringTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public async Task MarketDataFetcher_NetworkDrop_ShouldFallbackAndNotCrash()
        {
            // Arrange (Chaos Setup)
            _output.WriteLine("[Chaos] Simulating 'Plug Pulled' during Binance request.");

            var mockHandler = new Mock<HttpMessageHandler>(MockBehavior.Strict);

            // Setup Binance to fail with a network exception (simulating lost internet)
            mockHandler.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.Is<HttpRequestMessage>(req => req.RequestUri.Host.Contains("api.binance.com")),
                    ItExpr.IsAny<CancellationToken>()
                )
                .ThrowsAsync(new HttpRequestException("Simulated Network Drop - The internet is gone!"));

            // Setup TwelveData to also fail or return empty just to ensure we don't crash, 
            // or let it return a dummy response.
            mockHandler.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.Is<HttpRequestMessage>(req => req.RequestUri.Host.Contains("api.twelvedata.com")),
                    ItExpr.IsAny<CancellationToken>()
                )
                .ReturnsAsync(new HttpResponseMessage
                {
                    StatusCode = System.Net.HttpStatusCode.OK,
                    Content = new StringContent("{\"status\":\"ok\",\"values\":[{\"open\":\"1\",\"high\":\"1.5\",\"low\":\"0.5\",\"close\":\"1.2\",\"volume\":\"100\",\"datetime\":\"2024-01-01\"}]}")
                });

            var httpClient = new HttpClient(mockHandler.Object);
            
            var mockFactory = new Mock<IHttpClientFactory>();
            mockFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);

            // Inject the chaotic factory globally (since the codebase uses static property)
            MiniAppController.HttpFactory = mockFactory.Object;

            var fetcher = MarketDataFetcher.Instance;

            // Act
            Exception capturedEx = null;
            double[] prices = null;
            try
            {
                var result = await fetcher.FetchBinanceWithFallback("BTCUSDT", "m1", "BTC/USD");
                prices = result.prices;
            }
            catch (Exception ex)
            {
                capturedEx = ex;
            }

            // Assert (The Judgement)
            Assert.Null(capturedEx); // Should not crash!
            Assert.NotNull(prices);
            
            _output.WriteLine("[Chaos] System survived! Fallback logic executed successfully without fatal crash.");
        }
    }
}

#endif
