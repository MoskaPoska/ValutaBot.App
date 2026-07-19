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
            string clean1 = MiniAppController.SanitizeAsset("EUR/USD OTC");
            string clean2 = MiniAppController.SanitizeAsset("EUR/USD ОТС"); // Cyrillic
            string clean3 = MiniAppController.SanitizeAsset("  GBP-USD  ");
            
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
            
            var hurstMethod = typeof(MiniAppController).GetMethod("CalculateHurstExponent", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            double trendHurst = (double)hurstMethod.Invoke(null, new object[] { trendPrices })!;

            // Generate range prices (sine wave): H should be low (<0.45)
            double[] rangePrices = new double[50];
            for (int i = 0; i < 50; i++) rangePrices[i] = 1.0 + Math.Sin(i * 0.5) * 0.1;
            double rangeHurst = (double)hurstMethod.Invoke(null, new object[] { rangePrices })!;

            Assert("Hurst trending detection", trendHurst > 0.55, $"Expected H > 0.55 for linear trend, got {trendHurst:F2}");
            Assert("Hurst mean-reverting detection", rangeHurst < 0.45, $"Expected H < 0.45 for sine wave, got {rangeHurst:F2}");

            // ─── 3. TEST KALMAN FILTER NOISE REDUCTION ───
            Console.WriteLine("\n[3] Testing Kalman Filter Noise Reduction...");
            
            // Generate noisy data around a constant value
            double[] noisyPrices = new double[60];
            var rand = new Random(42);
            for (int i = 0; i < 60; i++) noisyPrices[i] = 100.0 + (rand.NextDouble() - 0.5) * 10.0;

            var kalmanMethod = typeof(MiniAppController).GetMethod("ComputeKalmanFilter", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            double[] filteredPrices = (double[])kalmanMethod.Invoke(null, new object[] { noisyPrices })!;

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

            var tdMethod = typeof(MiniAppController).GetMethod("ComputeDeMarkScore", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            double tdScore = (double)tdMethod.Invoke(null, new object[] { droppingPrices })!;

            Assert("TD Sequential Buy Setup completion", tdScore == 0.35, $"Expected score 0.35, got {tdScore}");

            // ─── 5. TEST DATA FETCH AND REAL-TIME SYMBOLS (BINANCE & FALLBACK) ───
            Console.WriteLine("\n[5] Testing live Binance data retrieval & validation...");
            var options = new JsonSerializerOptions { WriteIndented = true };
            
            // Test weekend fallback for EUR/USD
            Console.WriteLine("Fetching EUR/USD (simulated weekend fallback)...");
            var res = await MiniAppController.ExecuteBinanceAnalysis("EUR/USD OTC", "m1");
            string resJson = JsonSerializer.Serialize(res, options);
            
            Assert("EUR/USD OTC fetching", resJson.Contains("direction") && !resJson.Contains("error"));

            // Check details of the result for NaNs or Infinities
            bool containsNaN = resJson.Contains("NaN") || resJson.Contains("Infinity");
            Assert("No NaN or Infinity in outputs", !containsNaN, "Verify output serialization contains valid numeric values");
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
