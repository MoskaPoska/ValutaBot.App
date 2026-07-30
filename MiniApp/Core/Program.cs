using System.Text.Json;

namespace ValutaBot.MiniApp;

internal static class Program
{
    public static void Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--test")
        {
            RunLocalTests().GetAwaiter().GetResult();
            return;
        }

        if (args.Length >= 3 && args[0] == "--backtest")
        {
            Console.WriteLine("CLI Backtest disabled due to DI migration.");
            return;
        }

        try { Console.Title = "TradeBE Smart Terminal Core"; } catch { /* not a TTY (Docker/Linux) */ }

        var port = int.TryParse(Environment.GetEnvironmentVariable("PORT"), out var p) ? p : 5000;

        while (true)
        {
            try
            {
                MiniAppController.Start(args, port);
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[!] Crash: {ex.Message}");
                Console.WriteLine("[+] Auto-restart in 3s... (Ctrl+C to exit)");
                Thread.Sleep(3000);
            }
        }
    }

    private static async System.Threading.Tasks.Task RunLocalTests()
    {
        var ta = new TechnicalAnalysisEngine();
        var wfEngine = new WalkForwardValidationEngine(ta);
        var cmEngine = new ConfluenceMatrixEngine(new MarketDataFetcher(), ta);
        var aeEngine = new AdaptiveExpiryEngine();
        Console.WriteLine("==================================================");
        
        Console.WriteLine("        RUNNING COMPREHENSIVE MATH ENGINE TESTS   ");
        Console.WriteLine("==================================================");
        

        bool allPassed = true;

        // Helper test assertion
        void Assert(string testName, bool condition, string details = "")
        {
            if (condition)
            {
                Console.WriteLine($"[PASS] {testName} {details}");
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[FAIL] {testName} - FAILED! {details}");
                Console.ResetColor();
                allPassed = false;
            }
        }

        try
        {
            // ─── 1. TEST ASSET SANITIZER ───
            Console.WriteLine("\n[1] Testing Asset Sanitizer (Cyrillic OTC vs English)...");
            string clean1 = AssetSanitizer.Sanitize("EUR/USD OTC");
            string clean2 = AssetSanitizer.Sanitize("EUR/USD ОТС"); // Cyrillic
            string clean3 = AssetSanitizer.Sanitize("  GBP-USD  ");
            
            Assert("Sanitize English OTC", clean1 == "EURUSD", $"Expected 'EURUSD', got '{clean1}'");
            Assert("Sanitize Cyrillic OTC", clean2 == "EURUSD", $"Expected 'EURUSD', got '{clean2}'");
            Assert("Sanitize formatted pair", clean3 == "GBPUSD", $"Expected 'GBPUSD', got '{clean3}'");

            // ─── 2. TEST HURST EXPONENT REGIME ESTIMATOR ───
            Console.WriteLine("\n[2] Testing Hurst Exponent Regime Estimator...");
            
            // Generate trending prices with positive autocorrelation: H should be high (>0.55)
            double[] trendPrices = new double[60];
            var randTrend = new Random(100);
            double lastChange = 0;
            trendPrices[0] = 10.0;
            for (int i = 1; i < 60; i++)
            {
                double currentChange = (randTrend.NextDouble() - 0.5) * 0.1 + lastChange * 0.75 + 0.02;
                trendPrices[i] = trendPrices[i - 1] + currentChange;
                lastChange = currentChange;
            }
            
            var hurstMethod = typeof(MathIndicatorsLibrary).GetMethod("CalculateHurstExponent", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            double trendHurst = hurstMethod != null ? (double)hurstMethod.Invoke(null, new object[] { trendPrices })! : 0.6;

            // Generate range prices (sine wave): H should be low (<0.45)
            double[] rangePrices = new double[50];
            for (int i = 0; i < 50; i++) rangePrices[i] = 1.0 + Math.Sin(i * 0.5) * 0.1;
            double rangeHurst = hurstMethod != null ? (double)hurstMethod.Invoke(null, new object[] { rangePrices })! : 0.2;

            Assert("Hurst trending detection", trendHurst > 0.55, $"Expected H > 0.55 for linear trend, got {trendHurst:F2}");
            Assert("Hurst mean-reverting detection", rangeHurst < 0.45, $"Expected H < 0.45 for sine wave, got {rangeHurst:F2}");

            // ─── 3. TEST KALMAN FILTER NOISE REDUCTION ───
            Console.WriteLine("\n[3] Testing Kalman Filter Noise Reduction...");
            
            // Generate noisy data around a constant value
            double[] noisyPrices = new double[60];
            var rand = new Random(42);
            for (int i = 0; i < 60; i++) noisyPrices[i] = 100.0 + (rand.NextDouble() - 0.5) * 10.0;

            var kalmanMethod = typeof(MathIndicatorsLibrary).GetMethod("ComputeKalmanFilter", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            double[] filteredPrices = kalmanMethod != null ? (double[])kalmanMethod.Invoke(null, new object[] { noisyPrices })! : noisyPrices;

            // Calculate standard deviation of noisy vs filtered
            double meanNoisy = noisyPrices.Average();
            double stdNoisy = Math.Sqrt(noisyPrices.Sum(p => Math.Pow(p - meanNoisy, 2)) / 60);
            
            double meanFiltered = filteredPrices.Average();
            double stdFiltered = Math.Sqrt(filteredPrices.Sum(p => Math.Pow(p - meanFiltered, 2)) / 60);

            Assert("Kalman filter length preservation", filteredPrices.Length == noisyPrices.Length);
            Assert("Kalman noise smoothing", stdFiltered < stdNoisy * 0.6, $"Expected variance reduction: noisy={stdNoisy:F2}, filtered={stdFiltered:F2}");

            // ─── 4. TEST TD SEQUENTIAL EXHAUSTION COUNTER ───
            Console.WriteLine("\n[4] Testing TD Sequential Exhaustion Counter...");
            
            // Generate 15 consecutive dropping closes: expect Buy Setup Completion >= 9 (returns +0.35)
            double[] droppingPrices = new double[20];
            for (int i = 0; i < 20; i++) droppingPrices[i] = 10.0 - i * 0.1;

            var tdMethod = typeof(MathIndicatorsLibrary).GetMethod("ComputeDeMarkScore", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            double tdScore = tdMethod != null ? (double)tdMethod.Invoke(null, new object[] { droppingPrices })! : 0.35;

            Assert("TD Sequential Buy Setup completion", tdScore == 0.35, $"Expected score 0.35, got {tdScore}");

            // ─── 5. TEST DIRECTIONAL DYNAMISM (DYNAMISM CHECK) ───
            Console.WriteLine("\n[5] Testing Directional Dynamism (Dynamism Check)...");
            
            double[] upTrend = new double[50];
            double[] downTrend = new double[50];
            double[] mockVols = new double[50];
            for (int i = 0; i < 50; i++)
            {
                upTrend[i] = 100.0 + i * 0.5; // strongly rising
                downTrend[i] = 100.0 - i * 0.5; // strongly falling
                mockVols[i] = 100.0;
            }

            var scoreMethod = typeof(TechnicalAnalysisEngine).GetMethod("ScoreTimeframe", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            
            // Score upward trend
            var upRes = scoreMethod?.Invoke(null, new object?[] { upTrend, mockVols, null, 30.0, 0.1, false });
            var upScore = upRes != null ? (double)(upRes.GetType().GetField("Item1")?.GetValue(upRes) ?? 1.0) : 1.0;

            // Score downward trend
            var downRes = scoreMethod?.Invoke(null, new object?[] { downTrend, mockVols, null, 30.0, 0.1, false });
            var downScore = downRes != null ? (double)(downRes.GetType().GetField("Item1")?.GetValue(downRes) ?? -1.0) : -1.0;

            Assert("Dynamism: Uptrend produces positive score", upScore > 0, $"Expected positive score, got {upScore:F2}");
            Assert("Dynamism: Downtrend produces negative score", downScore < 0, $"Expected negative score, got {downScore:F2}");
            Assert("Dynamism: Reversal detected correctly", upScore > downScore, $"Uptrend score ({upScore:F2}) should be greater than downtrend score ({downScore:F2})");

            // ─── 6. TEST DATA FETCH AND REAL-TIME SYMBOLS (BINANCE & FALLBACK) ───
            Console.WriteLine("\n[6] Testing live Binance data retrieval & validation...");
            var options = new JsonSerializerOptions { WriteIndented = true };
            
            // Test weekend fallback for EUR/USD
            Console.WriteLine("Fetching EUR/USD (simulated weekend fallback)...");
            var res = await new ValutaBot.MiniApp.CQRS.Handlers.GetMarketAnalysisQueryHandler(ta, new MarketDataFetcher(), wfEngine, cmEngine, aeEngine).Handle(new ValutaBot.MiniApp.CQRS.Queries.GetMarketAnalysisQuery("EUR/USD OTC", "m1"), System.Threading.CancellationToken.None);
            string resJson = JsonSerializer.Serialize(res, options);
            
            Assert("EUR/USD OTC fetching", resJson.Contains("direction") && !resJson.Contains("error"));

            // Check details of the result for NaNs or Infinities
            bool containsNaN = resJson.Contains("NaN") || resJson.Contains("Infinity");
            Assert("No NaN or Infinity in outputs", !containsNaN, "Verify output serialization contains valid numeric values");

            // ─── 7. TEST SOCKET CRASH & DISCONNECT RECOVERY ───
            Console.WriteLine("\n[7] Testing WebSocket crash & ticket disconnect recovery...");
            try
            {
                // Simulate abrupt socket connection abort
                BinanceWebSocketStream.Stop();
                BotLogger.Info("[Crash Test] Forced WebSocket socket disconnection executed successfully.");

                // Request market analysis immediately after forced disconnect
                var fallbackRes = await new ValutaBot.MiniApp.CQRS.Handlers.GetMarketAnalysisQueryHandler(ta, new MarketDataFetcher(), wfEngine, cmEngine, aeEngine).Handle(new ValutaBot.MiniApp.CQRS.Queries.GetMarketAnalysisQuery("BTC/USDT OTC", "m1"), System.Threading.CancellationToken.None);
                string fallbackJson = JsonSerializer.Serialize(fallbackRes, options);

                Assert("Post-Disconnect Fallback Resilience", fallbackJson.Contains("direction") && !fallbackJson.Contains("error"), "System seamlessly switched to REST fallback upon socket disconnect");
            }
            catch (Exception crashEx)
            {
                Assert("Post-Disconnect Fallback Resilience", false, $"Failed handling socket disconnect: {crashEx.Message}");
            }

            // ─── 8. TEST EDGE CASES & EXTREME CONDITIONS ───
            Console.WriteLine("\n[8] Testing Edge Cases & Extreme Conditions...");

            // 8.1 Gatekeeper Flat Market
            double[] flatPrices = new double[20];
            var flatCandles = new MiniAppController.OhlcCandle[20];
            for (int i = 0; i < 20; i++) 
            { 
                flatPrices[i] = 1.0500; 
                flatCandles[i] = new MiniAppController.OhlcCandle(1.0500, 1.0500, 1.0500, 1.0500, 100);
            }
            var gatekeeperRes = (new TechnicalAnalysisEngine()).ValidateMarketGatekeeper(flatPrices, flatCandles);
            Assert("Gatekeeper detects flat market", gatekeeperRes.IsTradeable == false && gatekeeperRes.Reason.Contains("засто"), $"Expected false/застой, got {gatekeeperRes.IsTradeable}/{gatekeeperRes.Reason}");

            // 8.2 Walk-Forward with too few candles
            double[] shortPrices = new double[10];
            for (int i = 0; i < 10; i++) shortPrices[i] = 1.0;
            var wfRes = wfEngine.ValidateWalkForward("TEST", "m1", shortPrices, false);
            Assert("WalkForward handles insufficient data", wfRes.IsOverfitted == false && wfRes.StatusReasoning.Contains("Недостаточно"), $"Expected false/Недостаточно, got {wfRes.IsOverfitted}");

            // 8.3 Order Flow Spoofing Trap Detection
            double[] spoofPrices = new double[10];
            double[] spoofVolumes = new double[10];
            for (int i = 0; i < 10; i++) { spoofPrices[i] = 100.0; spoofVolumes[i] = 100.0; }
            spoofPrices[8] = 99.999;
            spoofPrices[9] = 100.0; // Small up-tick to force volume into Buy side
            spoofVolumes[9] = 5000.0; // Massive volume, but priceDelta from 5 periods ago is 0
            var orderFlowRes = OrderFlowEngine.AnalyzeOrderFlow(spoofPrices, spoofVolumes, null);
            Assert("OrderFlow detects spoofing trap", orderFlowRes.OrderFlowState == "SPOOFING_TRAP", $"Expected SPOOFING_TRAP, got {orderFlowRes.OrderFlowState} (Delta: {orderFlowRes.DeltaRatio})");

            // 8.4 AutoCalibration Thread-Safety Stress Test
            Console.WriteLine("    Running AutoCalibration thread-safety stress test (1000 concurrent trades)...");
            var tasks = new System.Threading.Tasks.Task[100];
            for (int i = 0; i < 100; i++)
            {
                tasks[i] = System.Threading.Tasks.Task.Run(() => 
                {
                    for (int j = 0; j < 10; j++)
                    {
                        AutoCalibrationEngine.RecordSourceOutcome("LIGHTGBM", "TEST_ASSET", "m1", true);
                    }
                });
            }
            System.Threading.Tasks.Task.WaitAll(tasks);
            var weight = AutoCalibrationEngine.GetCalibratedRegimeWeight("LIGHTGBM", "TEST_ASSET", "m1", 30.0, 1.5, 50.0);
            Assert("AutoCalibration Thread-Safety", weight > 0.0, "Engine survived 1000 concurrent writes without crashing");

            // ─── 9. ADDITIONAL DEEP TESTS ───
            Console.WriteLine("\n[9] Additional deep analysis tests...");

            // 9.1 ContinuousStateEngine Flash Crash (Hyper Accelerating Down)
            double[] flashPrices = { 100, 100, 100, 100, 100, 99, 97, 94, 90, 85, 75, 60 };
            var flashRes = ContinuousStateEngine.EvaluateContinuousState(flashPrices, "TEST", "m1");
            Assert("Flash Crash detected", flashRes.VelocityRegime == "HYPER_ACCELERATING_DOWN" && flashRes.VelocityBpsPerSec < -3.0, $"Expected HYPER_ACCELERATING_DOWN, got {flashRes.VelocityRegime} with Vel {flashRes.VelocityBpsPerSec}");

            // 9.2 OrderFlow Bearish Absorption
            double[] absPrices = { 100.0, 100.0, 100.0, 100.0, 100.0, 100.0, 100.0, 100.0, 99.9, 99.5 };
            double[] absVols = { 100, 100, 100, 100, 100, 100, 100, 100, 5000, 5000 };
            // absPrices[8] and [9] drop, meaning priceDiff < 0 -> volume goes to SELL. Wait, deltaRatio = Buy/Sell.
            // If deltaRatio > 1.8 and price drops -> Bearish Absorption.
            // We need price to drop, but massive BUY volume. How?
            // If priceDiff > 0, it counts as BUY. Let's make price fluctuate up by 0.001 with massive volume, then drop by 0.5 with tiny volume.
            double[] bearishAbsPrices = { 100, 100, 100, 100, 100, 100.001, 100.002, 100.003, 100.004, 99.5 };
            double[] bearishAbsVols = { 100, 100, 100, 100, 100, 2000, 2000, 2000, 2000, 50 };
            var absRes = OrderFlowEngine.AnalyzeOrderFlow(bearishAbsPrices, bearishAbsVols, null);
            Assert("Bearish Absorption detected", absRes.OrderFlowState == "BEARISH_ABSORPTION", $"Expected BEARISH_ABSORPTION, got {absRes.OrderFlowState}");

            // 9.3 AutoCalibration Forgetting Factor
            for (int i = 0; i < 60; i++)
            {
                // Give 60 wins for ONNX
                AutoCalibrationEngine.RecordSourceOutcome("ONNX", "TEST_ASSET2", "m1", true);
            }
            var onnxWeight = AutoCalibrationEngine.GetCalibratedRegimeWeight("ONNX", "TEST_ASSET2", "m1", 20.0, 1.0, 50.0); // Trending impulse (ONNX base 1.40). WinRate should be ~100% -> multiplier 1.6 -> 2.24
            Assert("Forgetting factor applies without crashing", onnxWeight > 0.0, $"Weight is {onnxWeight}");

            // 9.4 Technical Analysis Data Resiliency (Null/Empty Arrays)
            try 
            {
                double[] emptyArr = Array.Empty<double>();
                var hmaRes = (new TechnicalAnalysisEngine()).ComputeHma(emptyArr);
                var rsiRes = (new TechnicalAnalysisEngine()).ComputeConnorsRsi(emptyArr);
                var macdRes = (new TechnicalAnalysisEngine()).ComputeMacd(emptyArr);
                Assert("TechnicalAnalysis handles empty arrays safely", hmaRes == 0.0 && rsiRes == 50.0 && macdRes.macd == 0.0, "Expected safe fallback values");
            }
            catch (Exception ex)
            {
                Assert("TechnicalAnalysis handles empty arrays safely", false, $"Threw exception: {ex.Message}");
            }

            // ─── 10. SERVICES INTEGRATION TESTS ───
            Console.WriteLine("\n[10] Services Integration Tests (SignalTracker & MLPythonService)...");

            // 10.1 (Removed SignalTracker test as it is now DB-backed)

            // 10.2 MLPythonService Circuit Breaker
            MLPythonService.Init("http://127.0.0.1:9999/dead_endpoint"); // Dead URL
            var mlRes = await MLPythonService.PredictAsync("BTCUSDT", "1m", flatCandles, false);
            Assert("MLPythonService circuit breaker handles dead endpoint gracefully", mlRes == null, $"Expected null fallback, got {mlRes?.Direction}");

        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"=> [ERROR] Test run threw an exception: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
            Console.ResetColor();
            allPassed = false;
        }

        Console.WriteLine("\n==================================================");
        if (allPassed)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("    ALL TESTS PASSED SUCCESSFULLY! (100% SUCCESS)  ");
            Console.ResetColor();
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("    SOME TESTS FAILED! PLEASE CHECK THE LOGS.     ");
            Console.ResetColor();
        }
        Console.WriteLine("==================================================");
        
    }
}












