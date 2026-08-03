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
using ValutaBot.MiniApp.CQRS.Handlers;

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

        [Fact]
        public async Task WalkForward_Overriding_ML_When_Overfitted_ShouldBlockSignal()
        {
            // Arrange
            _output.WriteLine("[Test] Starting 'Защита от переобучения' (Walk-Forward Overfitting Block) scenario...");

            var mockHandler = new Mock<HttpMessageHandler>(MockBehavior.Strict);

            // Mock Binance to return standard chart data
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
            
            // Mock TwelveData as fallback (or primary for weekday)
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

            var httpClient = new HttpClient(mockHandler.Object);
            var mockFactory = new Mock<IHttpClientFactory>();
            mockFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);
            MiniAppController.HttpFactory = mockFactory.Object;

            var taEngine = new TechnicalAnalysisEngine();
            var wfEngine = new WalkForwardValidationEngine(taEngine);

            // SIMULATE 3 CONSECUTIVE LOSSES TO TRIGGER WALK-FORWARD COOLOFF
            wfEngine.RecordTradeOutcome("EURUSD", "m5", false);
            wfEngine.RecordTradeOutcome("EURUSD", "m5", false);
            wfEngine.RecordTradeOutcome("EURUSD", "m5", false);

            var cmEngine = new ConfluenceMatrixEngine();
            var aeEngine = new AdaptiveExpiryEngine();
            var fetcher = MarketDataFetcher.Instance;

            var handler = new GetMarketAnalysisQueryHandler(cmEngine, aeEngine, wfEngine, fetcher, taEngine);
            var context = new MarketAnalysisContext(handler, "EURUSD", "m5");

            // Act
            var resultObj = await context.ExecuteAnalysisAsync();
            var resultStr = System.Text.Json.JsonSerializer.Serialize(resultObj);
            
            _output.WriteLine($"[Test] Final Result String: {resultStr}");

            // Assert
            // Because WalkForward triggered a cooloff, the Kill Switch must force direction to NEUTRAL and probability to 0.
            Assert.Contains("\"direction\":\"NEUTRAL\"", resultStr);
            Assert.Contains("\"probability\":0", resultStr);
            Assert.Contains("\"wfIsCooloffActive\":true", resultStr);
        }

        [Fact]
        public async Task TA_vs_SMC_BullTrap_ShouldOverrideTaWithSmc()
        {
            // Arrange
            _output.WriteLine("[Test] Starting 'Бычья ловушка' (TA vs SMC Conflict) scenario...");

            var mockHandler = new Mock<HttpMessageHandler>(MockBehavior.Strict);

            // Mock Binance to return a Bull Trap pattern
            string binanceResponse = GenerateBullTrapJson(150);
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

            var taEngine = new TechnicalAnalysisEngine();
            var wfEngine = new WalkForwardValidationEngine(taEngine);
            var cmEngine = new ConfluenceMatrixEngine(MarketDataFetcher.Instance, taEngine);
            var aeEngine = new AdaptiveExpiryEngine();
            var fetcher = MarketDataFetcher.Instance;

            var handler = new GetMarketAnalysisQueryHandler(cmEngine, aeEngine, wfEngine, fetcher, taEngine);
            var context = new MarketAnalysisContext(handler, "EURUSD", "m5");

            // Act
            var resultObj = await context.ExecuteAnalysisAsync();
            var resultStr = System.Text.Json.JsonSerializer.Serialize(resultObj);
            
            _output.WriteLine($"[Test] Final Result String: {resultStr}");

            // Assert
            // The RSI will be extremely high (TA = BUY), but SMC detects a Bearish Order Block and Bearish Sweep.
            // SMC should override TA or at least drag the probability down heavily, resulting in a PUT or NEUTRAL.
            Assert.DoesNotContain("\"direction\":\"BUY\"", resultStr); // Must not be fooled by the trap!
            Assert.Contains("Bearish Sweep", resultStr); // LLM Report or SMC Reasoning should mention it
        }

        private string GenerateBullTrapJson(int count)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("[");
            double price = 1.1000;
            for (int i = 0; i < count; i++)
            {
                if (i < count - 4)
                {
                    // Aggressive uptrend to force RSI > 80 (Strong TA BUY)
                    double open = price;
                    price += 0.0010;
                    double close = price;
                    double high = close + 0.0002;
                    double low = open - 0.0002;
                    sb.Append($"[1600000000000,\"{open}\",\"{high}\",\"{low}\",\"{close}\",\"100\",1600000059999,\"0\",100,\"0\",\"0\",\"0\"]");
                }
                else if (i == count - 4) // c1: The peak (Bullish candle)
                {
                    double open = price;
                    double high = price + 0.0050; // Massive peak
                    double close = price + 0.0040;
                    double low = open - 0.0001;
                    price = close;
                    sb.Append($"[1600000000000,\"{open}\",\"{high}\",\"{low}\",\"{close}\",\"100\",1600000059999,\"0\",100,\"0\",\"0\",\"0\"]");
                }
                else if (i == count - 3) // c2: Bearish Sweep & Displacement
                {
                    double open = price;
                    double high = price + 0.0020; // Sweep higher high
                    double close = price - 0.0100; // Massive drop (Displacement)
                    double low = close - 0.0010;
                    price = close;
                    sb.Append($"[1600000000000,\"{open}\",\"{high}\",\"{low}\",\"{close}\",\"100\",1600000059999,\"0\",100,\"0\",\"0\",\"0\"]");
                }
                else if (i == count - 2) // c3: FVG Gap confirmation
                {
                    double open = price;
                    double close = price - 0.0050; // Continues down, leaving a gap between c1 Low and c3 High
                    double high = open + 0.0005;
                    double low = close - 0.0005;
                    price = close;
                    sb.Append($"[1600000000000,\"{open}\",\"{high}\",\"{low}\",\"{close}\",\"100\",1600000059999,\"0\",100,\"0\",\"0\",\"0\"]");
                }
                else // c4: Current candle
                {
                    double open = price;
                    double close = price;
                    double high = open + 0.0001;
                    double low = open - 0.0001;
                    sb.Append($"[1600000000000,\"{open}\",\"{high}\",\"{low}\",\"{close}\",\"100\",1600000059999,\"0\",100,\"0\",\"0\",\"0\"]");
                }

                if (i < count - 1) sb.Append(",");
            }
            sb.Append("]");
            return sb.ToString();
        }

        [Fact]
        public async Task OrderFlow_vs_ContinuousState_BullishAbsorption_ShouldOverrideFlatState()
        {
            // Arrange
            _output.WriteLine("[Test] Starting 'Скрытое накопление' (OrderFlow vs ContinuousState) scenario...");

            var mockHandler = new Mock<HttpMessageHandler>(MockBehavior.Strict);
            string binanceResponse = GenerateBullishAbsorptionJson(150);
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

            var taEngine = new TechnicalAnalysisEngine();
            var wfEngine = new WalkForwardValidationEngine(taEngine);
            var cmEngine = new ConfluenceMatrixEngine(MarketDataFetcher.Instance, taEngine);
            var aeEngine = new AdaptiveExpiryEngine();
            var fetcher = MarketDataFetcher.Instance;

            var handler = new GetMarketAnalysisQueryHandler(cmEngine, aeEngine, wfEngine, fetcher, taEngine);
            var context = new MarketAnalysisContext(handler, "EURUSD", "m5");

            // Act
            var resultObj = await context.ExecuteAnalysisAsync();
            var resultStr = System.Text.Json.JsonSerializer.Serialize(resultObj);
            
            _output.WriteLine($"[Test] Final Result String: {resultStr}");

            // Assert
            // The ContinuousState is STABLE, but OrderFlow detects massive BULLISH ABSORPTION.
            Assert.Contains("Bullish Absorption", resultStr);
        }

        [Fact]
        public async Task AdaptiveExpiry_VolatilitySpike_ShouldIncreaseExpiry()
        {
            // Arrange
            _output.WriteLine("[Test] Starting 'Штормовое предупреждение' (Adaptive Expiry) scenario...");

            var mockHandler = new Mock<HttpMessageHandler>(MockBehavior.Strict);
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

            // MOCK TA Engine to simulate extremely high volatility (VolRatio > 1.5)
            var mockTaEngine = new Mock<ITechnicalAnalysisEngine>();
            mockTaEngine.Setup(x => x.ValidateMarketGatekeeper(It.IsAny<double[]>(), It.IsAny<MiniAppController.OhlcCandle[]>()))
                .Returns(new TechnicalAnalysisEngine.GatekeeperResult(true, "Mock"));
            mockTaEngine.Setup(x => x.ScoreTimeframe(It.IsAny<double[]>(), It.IsAny<double[]>(), It.IsAny<MiniAppController.OhlcCandle[]>(), It.IsAny<double?>(), It.IsAny<double?>(), It.IsAny<bool>()))
                .Returns((0.5, 90.0, 60.0, 1.1000, 1.0, 0.0010));
            mockTaEngine.Setup(x => x.CalculateVolatilityRatio(It.IsAny<double[]>())).Returns(2.5); // HIGH VOLATILITY

            // Also mock ComputeTrueAdx and ComputeAtr which are called by the context
            mockTaEngine.Setup(x => x.ComputeTrueAdx(It.IsAny<MiniAppController.OhlcCandle[]>(), 14))
                .Returns((25.0, 10.0, 5.0));
            mockTaEngine.Setup(x => x.ComputeAtr(It.IsAny<MiniAppController.OhlcCandle[]>(), 14))
                .Returns(0.0020);

            var wfEngine = new WalkForwardValidationEngine(mockTaEngine.Object);
            var cmEngine = new ConfluenceMatrixEngine(MarketDataFetcher.Instance, mockTaEngine.Object);
            var aeEngine = new AdaptiveExpiryEngine();
            var fetcher = MarketDataFetcher.Instance;

            var handler = new GetMarketAnalysisQueryHandler(cmEngine, aeEngine, wfEngine, fetcher, mockTaEngine.Object);
            var context = new MarketAnalysisContext(handler, "EURUSD", "m5"); // m5 standard expiry = 300s (5 min)

            // Act
            var resultObj = await context.ExecuteAnalysisAsync();
            var resultStr = System.Text.Json.JsonSerializer.Serialize(resultObj);
            
            _output.WriteLine($"[Test] Final Result String: {resultStr}");

            // Assert
            // Because VolRatio = 2.5 (> 1.5), the AdaptiveExpiryEngine should double the expiry from 5 to 10 minutes.
            // Check that the returned duration is "10 минут" or "10" candles depending on formatting.
            Assert.Contains("турбулент", resultStr); // The reasoning contains "Рынок турбулентный"
            Assert.Contains("\"expiryCandles\":2", resultStr); // 10 minutes / 5 minute timeframe = 2 candles
        }

        private string GenerateBullishAbsorptionJson(int count)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("[");
            double price = 1.1000;
            for (int i = 0; i < count; i++)
            {
                double open = price;
                double close = price + 0.00001; // tiny up drift
                double high = close + 0.00100; // huge upper wick -> selling pressure
                double low = open - 0.00001;
                
                double volume = (i >= count - 10) ? 5000 : 100;

                sb.Append($"[1600000000000,\"{open}\",\"{high}\",\"{low}\",\"{close}\",\"{volume}\",1600000059999,\"0\",100,\"0\",\"0\",\"0\"]");
                if (i < count - 1) sb.Append(",");
                price = close;
            }
            sb.Append("]");
            return sb.ToString();
        }

        [Fact]
        public async Task Gatekeeper_FlashCrash_ShouldBlockTrade()
        {
            // Arrange
            _output.WriteLine("[Test] Starting 'Flash Crash' (Gatekeeper) scenario...");

            var mockHandler = new Mock<HttpMessageHandler>(MockBehavior.Strict);
            string binanceResponse = GenerateFlashCrashJson(150);
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

            var taEngine = new TechnicalAnalysisEngine();
            var wfEngine = new WalkForwardValidationEngine(taEngine);
            var cmEngine = new ConfluenceMatrixEngine(MarketDataFetcher.Instance, taEngine);
            var aeEngine = new AdaptiveExpiryEngine();
            var fetcher = MarketDataFetcher.Instance;

            var handler = new GetMarketAnalysisQueryHandler(cmEngine, aeEngine, wfEngine, fetcher, taEngine);
            var context = new MarketAnalysisContext(handler, "EURUSD", "m5");

            // Act
            var resultObj = await context.ExecuteAnalysisAsync();
            var resultStr = System.Text.Json.JsonSerializer.Serialize(resultObj);
            
            _output.WriteLine($"[Test] Final Result String: {resultStr}");

            // Assert
            // Ensure the Gatekeeper catches the flash crash and blocks trading.
            Assert.Contains("Flash Crash", resultStr);
            Assert.Contains("\"direction\":\"NEUTRAL\"", resultStr);
        }

        [Fact]
        public async Task MlServiceCrash_ShouldDegradeGracefully()
        {
            // Arrange
            _output.WriteLine("[Test] Starting 'ML Service Crash' scenario...");

            var mockHandler = new Mock<HttpMessageHandler>(MockBehavior.Strict);
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
            
            // Mock ML Service to throw an exception / return 500
            mockHandler.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.Is<HttpRequestMessage>(req => req.RequestUri.Host.Contains("localhost") || req.RequestUri.Port == 5001),
                    ItExpr.IsAny<CancellationToken>()
                )
                .ThrowsAsync(new HttpRequestException("Connection refused"));

            var httpClient = new HttpClient(mockHandler.Object);
            var mockFactory = new Mock<IHttpClientFactory>();
            mockFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);
            MiniAppController.HttpFactory = mockFactory.Object;

            var taEngine = new TechnicalAnalysisEngine();
            var wfEngine = new WalkForwardValidationEngine(taEngine);
            var cmEngine = new ConfluenceMatrixEngine(MarketDataFetcher.Instance, taEngine);
            var aeEngine = new AdaptiveExpiryEngine();
            var fetcher = MarketDataFetcher.Instance;

            var handler = new GetMarketAnalysisQueryHandler(cmEngine, aeEngine, wfEngine, fetcher, taEngine);
            var context = new MarketAnalysisContext(handler, "EURUSD", "m5");

            // Act
            var resultObj = await context.ExecuteAnalysisAsync();
            var resultStr = System.Text.Json.JsonSerializer.Serialize(resultObj);
            
            _output.WriteLine($"[Test] Final Result String: {resultStr}");

            // Assert
            // The bot should NOT crash. It should fall back gracefully.
            // ML probability might be 0, but the overall pipeline should return a valid object.
            Assert.DoesNotContain("Connection refused", resultStr); // Stack traces shouldn't leak
            Assert.Contains("\"direction\"", resultStr); // We still have a signal output (can be NEUTRAL or based purely on TA)
        }

        private string GenerateFlashCrashJson(int count)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("[");
            double price = 1.1000;
            for (int i = 0; i < count; i++)
            {
                double open = price;
                double close = price + 0.0001; 
                double high = close + 0.0001; 
                double low = open - 0.0001;
                
                // FLASH CRASH on the very last candle
                if (i == count - 1)
                {
                    high = open;
                    close = open - 0.0500; // 500 pips drop in 1 candle!
                    low = close - 0.0010;
                }

                sb.Append($"[1600000000000,\"{open}\",\"{high}\",\"{low}\",\"{close}\",\"100\",1600000059999,\"0\",100,\"0\",\"0\",\"0\"]");
                if (i < count - 1) sb.Append(",");
                price = close;
            }
            sb.Append("]");
            return sb.ToString();
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

#endif
