using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
using ValutaBot.MiniApp;
using ValutaBot.MiniApp.CQRS.Handlers;
using ValutaBot.MiniApp.CQRS.Queries;
using Moq;

namespace ValutaBot.Tests
{
    public class ChaosEngineeringTests
    {
        private readonly ITestOutputHelper _output;

        public ChaosEngineeringTests(ITestOutputHelper output)
        {
            _output = output;
        }

        private GetMarketAnalysisQueryHandler GetHandler()
        {
            var mockFetcher = new Mock<MarketDataFetcher>();
            var mockCandles = new MiniAppController.OhlcCandle[100];
            var mockPrices = new double[100];
            var mockVolumes = new double[100];

            for(int i=0; i<100; i++) 
            {
                mockCandles[i] = new MiniAppController.OhlcCandle(1.0, 1.0, 1.0, 1.0, 100.0, DateTime.UtcNow);
                mockPrices[i] = i % 2 == 0 ? 1.0 : 1.1;
                mockVolumes[i] = 100.0;
            }
            
            mockFetcher.Setup(f => f.FetchOhlcWithFallbackAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()))
                       .ReturnsAsync(mockCandles);
                       
            mockFetcher.Setup(f => f.FetchBinanceWithFallback(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()))
                       .ReturnsAsync((mockPrices, mockVolumes));

            var taEngine = new TechnicalAnalysisEngine();
            var wfEngine = new WalkForwardValidationEngine();
            var cmEngine = new ConfluenceMatrixEngine(mockFetcher.Object, taEngine, new AutoCalibrationEngine());
            var timeoutEngine = new TradeTimeoutEngine();

            return new GetMarketAnalysisQueryHandler(taEngine, taEngine, taEngine, mockFetcher.Object, wfEngine, cmEngine, timeoutEngine, new MonteCarloEngine());
        }

        [Fact]
        public async Task PoisonData_DoesNotCrashApp()
        {
            _output.WriteLine("[Chaos] Injecting poison data (null, empty arrays, NaNs)...");

            var taEngine = new TechnicalAnalysisEngine();

            // 1. Technical Analysis Engine Chaos
            double[] emptyArray = Array.Empty<double>();
            double[] poisonArray = { double.NaN, double.PositiveInfinity, double.NegativeInfinity, 0 };

            var emptyTaRes = taEngine.ScoreTimeframe("EURUSD", "m5", emptyArray, emptyArray);
            Assert.Equal(0, emptyTaRes.score);
            Assert.Equal(50, emptyTaRes.confidence);

            var poisonTaRes = taEngine.ScoreTimeframe("EURUSD", "m5", poisonArray, poisonArray);
            Assert.Equal(0, poisonTaRes.score);
            Assert.Equal(50, poisonTaRes.confidence);
            
            // 2. OrderFlow Chaos (OrderFlowEngine is static)
            var emptyOfRes = OrderFlowEngine.AnalyzeOrderFlow("EURUSD", "m5", Array.Empty<MiniAppController.OhlcCandle>(), 100);
            Assert.Equal("BALANCED", emptyOfRes.OrderFlowState);
            Assert.Equal(0, emptyOfRes.ScoreContribution);

            // 3. ContinuousStateEngine Chaos (ContinuousStateEngine is static)
            var csRes = ContinuousStateEngine.EvaluateContinuousState(emptyArray, "EURUSD", "m5");
            Assert.Equal("UNKNOWN", csRes.VelocityRegime);
            
            _output.WriteLine("[Chaos] Poison data survived. Engines degraded gracefully.");
        }

        [Fact]
        public async Task ConcurrencyBombardment_SurvivesLoad()
        {
            _output.WriteLine("[Chaos] Bombarding GetMarketAnalysisQueryHandler with 100 concurrent requests...");

            var handler = GetHandler();
            var tasks = new Task<object>[100];
            
            var startSignal = new CountdownEvent(1);
            
            for (int i = 0; i < 100; i++)
            {
                tasks[i] = Task.Run(async () =>
                {
                    startSignal.Wait();
                    return await handler.Handle(new GetMarketAnalysisQuery("EURUSDT", "m5"), CancellationToken.None);
                });
            }

            // RELEASE THE HOUNDS
            startSignal.Signal();

            var results = await Task.WhenAll(tasks);

            Assert.Equal(100, results.Length);
            foreach (var res in results)
            {
                Assert.NotNull(res);
            }

            _output.WriteLine("[Chaos] Survived 100 concurrent requests perfectly.");
        }
    }
}

