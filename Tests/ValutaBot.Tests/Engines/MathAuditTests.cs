using System;
using Xunit;
using ValutaBot.MiniApp;
using ValutaBot.MiniApp.Indicators;

namespace ValutaBot.Tests.Engines
{
    public class MathAuditTests
    {
        private MiniAppController.OhlcCandle[] GenerateCandles(int count, double basePrice)
        {
            var candles = new MiniAppController.OhlcCandle[count];
            DateTime now = DateTime.UtcNow;
            for (int i = 0; i < count; i++)
            {
                candles[i] = new MiniAppController.OhlcCandle(
                    basePrice, basePrice + 0.0010, basePrice - 0.0010, basePrice, 100, now.AddMinutes(i - count)
                );
            }
            return candles;
        }

        [Fact]
        public void StatefulSmc_SweepLeakage_WithinSameLoop_Test()
        {
            var candles = GenerateCandles(20, 1.1000);
            
            // Create a swing high at index 10
            candles[10] = new MiniAppController.OhlcCandle(1.1000, 1.1050, 1.0990, 1.1000, 100, candles[10].Timestamp);
            
            // Create a sweep at index 14 (wick above index 10, close below)
            candles[14] = new MiniAppController.OhlcCandle(1.1000, 1.1060, 1.0990, 1.1000, 100, candles[14].Timestamp);
            
            var smc = new StatefulSmc();
            
            // Process up to index 12 to warm up
            var subset1 = new MiniAppController.OhlcCandle[14]; // i < 13
            Array.Copy(candles, subset1, 14);
            smc.Update(subset1, 1.1000);
            
            // Now a burst of data comes in (e.g. we were disconnected, or fetching multiple candles at startup).
            // We process up to index 18. 
            // The slice length is 19. Loop goes up to i < 18 (i.e. up to index 17).
            // Index 14 will have a sweep. Index 15, 16, 17 will NOT have a sweep.
            var subset2 = new MiniAppController.OhlcCandle[19];
            Array.Copy(candles, subset2, 19);
            
            smc.Update(subset2, 1.1000);
            
            // Expected: HasLiquiditySweep should be FALSE because the LAST processed candle (index 17) did not have a sweep.
            // If it's true, it means the sweep from index 14 leaked!
            Assert.False(smc.HasLiquiditySweep, "Stale sweep leaked within the same loop from an older candle!");
        }
    }
}
