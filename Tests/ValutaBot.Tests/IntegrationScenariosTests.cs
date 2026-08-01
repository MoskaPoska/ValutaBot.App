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
using ValutaBot.MiniApp.CQRS.Handlers;
using ValutaBot.App.MiniApp.Controllers;
using ValutaBot.App.MiniApp.Engines;

namespace ValutaBot.Tests
{
    public class IntegrationScenariosTests
    {
        private readonly ITestOutputHelper _output;

        public IntegrationScenariosTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public async Task PerfectStorm_ForexWeekday_ShouldGenerateFinalSignal()
        {
            // Arrange
            _output.WriteLine("[Test] Starting 'Идеальный шторм' (Forex) scenario...");

            var mockHandler = new Mock<HttpMessageHandler>(MockBehavior.Strict);

            // Mock TwelveData API with enough valid candles (e.g. 150) for the whole pipeline to work
            string twelveDataResponse = GenerateMockTwelveDataJson(150);
            mockHandler.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.Is<HttpRequestMessage>(req => req.RequestUri.Host.Contains("api.twelvedata.com")),
                    ItExpr.IsAny<CancellationToken>()
                )
                .ReturnsAsync(new HttpResponseMessage
                {
                    StatusCode = System.Net.HttpStatusCode.OK,
                    Content = new StringContent(twelveDataResponse)
                });

            // Mock Binance fallback (just in case it runs on a weekend during the test)
            string binanceResponse = GenerateMockBinanceJson(150);
            mockHandler.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.Is<HttpRequestMessage>(req => req.RequestUri.Host.Contains("api.binance.com")),
                    ItExpr.IsAny<CancellationToken>()
                )
                .ReturnsAsync(new HttpResponseMessage
                {
                    StatusCode = System.Net.HttpStatusCode.OK,
                    Content = new StringContent(binanceResponse)
                });

            var httpClient = new HttpClient(mockHandler.Object);
            var mockFactory = new Mock<IHttpClientFactory>();
            mockFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);
            MiniAppController.HttpFactory = mockFactory.Object;

            // Set environment variable to bypass API key check for TwelveData
            Environment.SetEnvironmentVariable("TwelveDataApiKey", "test-api-key");

            // Build Context Handler
            var cmEngine = new ConfluenceMatrixEngine();
            var aeEngine = new AdaptiveExpiryEngine();
            var wfEngine = new WalkForwardValidationEngine(new TechnicalAnalysisEngine());
            var fetcher = MarketDataFetcher.Instance;
            var taEngine = new TechnicalAnalysisEngine();

            var handler = new GetMarketAnalysisQueryHandler(cmEngine, aeEngine, wfEngine, fetcher, taEngine);
            
            // For Forex, "EURUSD" is the asset.
            var context = new MarketAnalysisContext(handler, "EURUSD", "m5");

            // Act
            object resultObj = null;
            Exception capturedEx = null;
            try
            {
                resultObj = await context.ExecuteAnalysisAsync();
            }
            catch (Exception ex)
            {
                capturedEx = ex;
                _output.WriteLine($"[Test] Pipeline crashed: {ex}");
            }

            // Assert
            Assert.Null(capturedEx);
            Assert.NotNull(resultObj);
            
            // Check that the returned object has the expected properties from the integration
            var resultStr = System.Text.Json.JsonSerializer.Serialize(resultObj);
            _output.WriteLine($"[Test] Final Result: {resultStr}");
            
            Assert.Contains("llmReport", resultStr);
            Assert.Contains("lgbmDirection", resultStr);
            Assert.Contains("Нейро-Анализ", resultStr);
        }

        [Fact]
        public async Task WeekendOTC_BinanceCrash_ShouldThrowExchangeUnavailableException()
        {
            // Arrange
            _output.WriteLine("[Test] Starting 'Субботний OTC' (Weekend Crash) scenario...");

            var mockHandler = new Mock<HttpMessageHandler>(MockBehavior.Strict);

            // Mock Binance to throw a network error
            mockHandler.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.Is<HttpRequestMessage>(req => req.RequestUri.Host.Contains("api.binance.com")),
                    ItExpr.IsAny<CancellationToken>()
                )
                .ThrowsAsync(new HttpRequestException("Binance is down!"));

            // Mock TwelveData to return empty/error since it's closed on weekends
            mockHandler.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.Is<HttpRequestMessage>(req => req.RequestUri.Host.Contains("api.twelvedata.com")),
                    ItExpr.IsAny<CancellationToken>()
                )
                .ReturnsAsync(new HttpResponseMessage
                {
                    StatusCode = System.Net.HttpStatusCode.OK,
                    Content = new StringContent("{\"status\":\"error\",\"message\":\"Market is closed\"}")
                });

            var httpClient = new HttpClient(mockHandler.Object);
            var mockFactory = new Mock<IHttpClientFactory>();
            mockFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);
            MiniAppController.HttpFactory = mockFactory.Object;

            var fetcher = MarketDataFetcher.Instance;

            // Act & Assert
            var ex = await Assert.ThrowsAsync<ExchangeUnavailableException>(async () =>
            {
                // Passing EURUSDT as the symbol (which is what AssetSanitizer would map EURUSD to on a weekend)
                await fetcher.FetchBinanceWithFallback("EURUSDT", "m1", "EUR/USD", 50);
            });

            _output.WriteLine($"[Test] Expected exception successfully caught: {ex.Message}");
            Assert.Contains("Биржа недоступна", ex.UserFriendlyMessage);
        }

        private string GenerateMockTwelveDataJson(int count)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("{\"status\":\"ok\",\"values\":[");
            double price = 1.1000;
            for (int i = 0; i < count; i++)
            {
                // Generate a slight uptrend
                price += 0.0001;
                sb.Append($"{{\"open\":\"{price - 0.0001}\",\"high\":\"{price + 0.0002}\",\"low\":\"{price - 0.0002}\",\"close\":\"{price}\",\"volume\":\"100\",\"datetime\":\"2024-01-01\"}}");
                if (i < count - 1) sb.Append(",");
            }
            sb.Append("]}");
            return sb.ToString();
        }

        private string GenerateMockBinanceJson(int count)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("[");
            double price = 1.1000;
            for (int i = 0; i < count; i++)
            {
                price += 0.0001;
                // Binance format: [openTime, open, high, low, close, volume, closeTime, quoteAssetVolume, trades, ...]
                sb.Append($"[1600000000000,\"{price - 0.0001}\",\"{price + 0.0002}\",\"{price - 0.0002}\",\"{price}\",\"100\",1600000059999,\"0\",100,\"0\",\"0\",\"0\"]");
                if (i < count - 1) sb.Append(",");
            }
            sb.Append("]");
            return sb.ToString();
        }
    }
}
