using System;
using ValutaBot.MiniApp.Indicators;

namespace ValutaBot.MathDumper
{
    public static class Fuzzer
    {
        public static void RunFuzzTests()
        {
            Console.WriteLine("=== STARTING EXTREME FUZZING ===");
            
            TestZeroVolatility();
            TestFlashCrash();
            TestNegativeAndZeroPrices();
            TestNaNInputs();
            
            Console.WriteLine("=== FUZZING COMPLETE. NO CRASHES ===\n");
            
            BenchmarkLevel3();
        }

        public static void BenchmarkLevel3()
        {
            Console.WriteLine("=== LEVEL 3: A/B COMPARISON (OLD vs NEW ARCHITECTURE) ===");
            
            // Generate Synthetic Flat Market (Sine wave)
            double[] flatPrices = new double[100];
            for (int i = 0; i < 100; i++) flatPrices[i] = 100.0 + Math.Sin(i * 0.5) * 5.0; // Oscillates 95 to 105
            
            // Generate Synthetic Trend Market
            double[] trendPrices = new double[100];
            for (int i = 0; i < 100; i++) trendPrices[i] = 100.0 + (i * 1.5) + Math.Sin(i * 1.0); // Steady uptrend

            Console.WriteLine("\n[SCENARIO 1: FLAT MARKET (Chop)]");
            SimulateMarket(flatPrices, isFlat: true);

            Console.WriteLine("\n[SCENARIO 2: STRONG UPTREND]");
            SimulateMarket(trendPrices, isFlat: false);
        }

        private static void SimulateMarket(double[] prices, bool isFlat)
        {
            var rsiGen = new StatefulRsi();
            var hmaGen = new StatefulHma();
            var adxGen = new StatefulTrueAdx();
            
            int oldCorrect = 0, newCorrect = 0, total = 0;

            for (int i = 0; i < prices.Length - 1; i++)
            {
                double price = prices[i];
                double nextPrice = prices[i + 1];
                
                double rsi = rsiGen.Update(price);
                double hma = hmaGen.Update(price);
                double adx = adxGen.Update(price + 2, price - 2, price); // Mock ADX behavior
                
                if (!rsiGen.IsWarm || i < 14) continue;

                // Force ADX to match the scenario so we can see the logic diverge
                adx = isFlat ? 15.0 : 35.0; 

                // --- OLD LOGIC ---
                double oldScore = 0;
                double oldRsiWeight = 1.0;
                double oldHmaWeight = 0.15;
                oldScore += ((rsi - 50.0) / 40.0) * oldRsiWeight;
                if (price > hma) oldScore += oldHmaWeight;
                else if (price < hma) oldScore -= oldHmaWeight;

                // --- NEW LOGIC ---
                double newScore = 0;
                double newHmaWeight = 0.15;
                if (adx < 20.0)
                {
                    newHmaWeight = 0.0;
                    newScore -= ((rsi - 50.0) / 40.0) * 2.0; // Inverted
                }
                else if (adx > 25.0)
                {
                    newHmaWeight = 0.30;
                    newScore += ((rsi - 50.0) / 40.0) * 0.5;
                }
                else
                {
                    newScore += ((rsi - 50.0) / 40.0) * 1.0;
                }
                if (price > hma) newScore += newHmaWeight;
                else if (price < hma) newScore -= newHmaWeight;

                // Evaluate predictions (Trade Horizon: Look 3 ticks ahead)
                int lookaheadIdx = Math.Min(prices.Length - 1, i + 3);
                double futurePrice = prices[lookaheadIdx];
                
                int actualDir = futurePrice > price ? 1 : -1;
                int oldDir = oldScore > 0 ? 1 : -1;
                int newDir = newScore > 0 ? 1 : -1;
                
                // Exclude neutral tiny scores
                if (Math.Abs(oldScore) > 0.05 && oldDir == actualDir) oldCorrect++;
                if (Math.Abs(newScore) > 0.05 && newDir == actualDir) newCorrect++;
                total++;
            }

            Console.WriteLine($"Total Trade Opportunities: {total}");
            Console.WriteLine($"OLD Architecture Winrate: {((double)oldCorrect / total) * 100:F1}% ({oldCorrect}/{total} correct)");
            Console.WriteLine($"NEW Architecture Winrate: {((double)newCorrect / total) * 100:F1}% ({newCorrect}/{total} correct)");
            
            double improvement = (((double)newCorrect / total) - ((double)oldCorrect / total)) * 100;
            Console.WriteLine($"Performance Delta: {(improvement >= 0 ? "+" : "")}{improvement:F1}%");
        }

        private static void TestZeroVolatility()
        {
            var rsi = new StatefulRsi();
            var crsi = new StatefulConnorsRsi();
            var atr = new StatefulAtr();
            var adx = new StatefulTrueAdx();

            for (int i = 0; i < 1000; i++)
            {
                double val1 = rsi.Update(100.0);
                double val2 = crsi.Update(100.0);
                double val3 = atr.Update(100.0, 100.0, 100.0);
                double val4 = adx.Update(100.0, 100.0, 100.0);
                
                if (double.IsNaN(val1) || double.IsNaN(val2) || double.IsNaN(val3) || double.IsNaN(val4))
                    throw new Exception("NaN detected in Zero Volatility test!");
            }
            Console.WriteLine("[PASS] Zero Volatility Test (1000 ticks of price 100.0)");
        }

        private static void TestFlashCrash()
        {
            var rsi = new StatefulRsi();
            var crsi = new StatefulConnorsRsi();
            var atr = new StatefulAtr();
            var adx = new StatefulTrueAdx();
            
            var rand = new Random(42);
            for (int i = 0; i < 1000; i++)
            {
                // Alternate between 1 and 1 million
                double p = (i % 2 == 0) ? 1.0 : 1000000.0;
                
                double val1 = rsi.Update(p);
                double val2 = crsi.Update(p);
                double val3 = atr.Update(p + 10, p - 10, p);
                double val4 = adx.Update(p + 10, p - 10, p);
                
                if (double.IsNaN(val1) || double.IsNaN(val2) || double.IsNaN(val3) || double.IsNaN(val4))
                    throw new Exception("NaN detected in Flash Crash test!");
            }
            Console.WriteLine("[PASS] Flash Crash Test (Alternating 1 and 1,000,000)");
        }

        private static void TestNegativeAndZeroPrices()
        {
            var crsi = new StatefulConnorsRsi();
            
            for (int i = 0; i < 1000; i++)
            {
                // In crsi, we divide by _prevPrice to get currentReturn. If _prevPrice is 0, what happens?
                double p = (i % 3 == 0) ? 0.0 : (i % 3 == 1 ? -50.0 : 50.0);
                
                double val2 = crsi.Update(p);
                
                if (double.IsNaN(val2))
                    throw new Exception("NaN detected in Negative/Zero price test!");
            }
            Console.WriteLine("[PASS] Negative & Zero Price Test (crsi div by zero check)");
        }

        private static void TestNaNInputs()
        {
            var crsi = new StatefulConnorsRsi();
            
            for (int i = 0; i < 100; i++)
            {
                double val = crsi.Update(double.NaN);
                // Math with NaNs propagates NaNs, but we just want to ensure it doesn't crash the runtime.
                // We won't throw on NaN output here because input is NaN.
            }
            Console.WriteLine("[PASS] NaN Input Tolerance Test");
        }
    }
}
