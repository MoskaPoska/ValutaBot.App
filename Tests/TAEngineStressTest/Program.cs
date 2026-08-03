using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using ValutaBot.MiniApp;
using ValutaBot.MiniApp.Indicators;

namespace TAEngineStressTest
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("=== Advanced Statistical Backtest: Module 1-6 (Trade Timeout Engine) ===");
            
            string dbPath = @"C:\Users\bural\source\repos\ValutaBot.App\ml_service\data\ValutaTicks.db";
            List<MiniAppController.OhlcCandle> allCandles = new List<MiniAppController.OhlcCandle>();
            List<double> closes = new List<double>();
            List<double> volumes = new List<double>();

            using (var connection = new SqliteConnection($"Data Source={dbPath}"))
            {
                connection.Open();
                var command = connection.CreateCommand();
                command.CommandText = "SELECT OpenTime, open, high, low, close, volume FROM HistoricalCandles WHERE Asset = 'EURUSD' ORDER BY OpenTime ASC LIMIT 50000";
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var ts = reader.GetString(0);
                        var dt = DateTime.Parse(ts);
                        var open = reader.GetDouble(1);
                        var high = reader.GetDouble(2);
                        var low = reader.GetDouble(3);
                        var close = reader.GetDouble(4);
                        var vol = reader.GetDouble(5);
                        allCandles.Add(new MiniAppController.OhlcCandle(open, high, low, close, vol, dt));
                        closes.Add(close);
                        volumes.Add(vol);
                    }
                }
            }

            var engine = TechnicalAnalysisEngine.Instance;
            var wfe = new WalkForwardValidationEngine(); // M4
            var timeoutEngine = new TradeTimeoutEngine(); // M6
            
            // Statistics trackers
            int totalTrades = 0;
            int totalWins = 0;
            int totalLosses = 0;
            
            double grossProfitPips = 0;
            double grossLossPips = 0;
            
            double peakBalance = 0;
            double currentBalance = 0;
            double maxDrawdown = 0;
            
            int tradesBlockedByCooloff = 0;
            int currentLossStreak = 0;
            int cooloffUntilIndex = -1;
            
            // M6 Expiry tracking
            Dictionary<int, int> expiryDistribution = new Dictionary<int, int>();
            int tradesExpiredByTime = 0;

            for (int i = 100; i < allCandles.Count - 60; i++)
            {
                if (i < cooloffUntilIndex)
                {
                    tradesBlockedByCooloff++; 
                    continue;
                }

                var candleSpan = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(allCandles).Slice(i - 100, 100);
                var priceSpan = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(closes).Slice(i - 100, 100);
                var volSpan = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(volumes).Slice(i - 100, 100);

                double entryPrice = closes[i - 1];

                // --- MODULE 5: Continuous State ---
                var continuousState = ContinuousStateEngine.EvaluateContinuousState(priceSpan, "EURUSD", "1m");

                // --- MODULE 1: Technical Analysis ---
                var taResult = engine.ScoreTimeframe("EURUSD", "1m", priceSpan, volSpan, candleSpan, isForex: true);
                
                double totalScore = taResult.score + continuousState.MomentumContribution;
                
                bool isLong = totalScore >= 0.7 && taResult.confidence >= 75.0;
                bool isShort = totalScore <= -0.7 && taResult.confidence >= 75.0;

                // --- MODULE 2: SMC ---
                var smcResult = SmcEngine.AnalyzeSmcStructure("EURUSD", "1m", candleSpan, entryPrice);
                
                bool smcBullish = (smcResult.HasFvg && smcResult.FvgType == "BULLISH_FVG") || 
                                  (smcResult.HasOrderBlock && smcResult.OrderBlockType == "BULLISH_OB") || 
                                  (smcResult.HasBos && smcResult.BosDirection == "BULLISH_BOS");
                                  
                bool smcBearish = (smcResult.HasFvg && smcResult.FvgType == "BEARISH_FVG") || 
                                  (smcResult.HasOrderBlock && smcResult.OrderBlockType == "BEARISH_OB") || 
                                  (smcResult.HasBos && smcResult.BosDirection == "BEARISH_BOS");

                isLong = isLong && smcBullish;
                isShort = isShort && smcBearish;

                // --- MODULE 3: Order Flow ---
                var ofResult = OrderFlowEngine.AnalyzeOrderFlow("EURUSD", "1m", candleSpan, entryPrice);
                
                if (isLong && (ofResult.OrderFlowState == "BEARISH_ABSORPTION" || ofResult.OrderFlowState == "SPOOFING_TRAP")) isLong = false;
                if (isShort && (ofResult.OrderFlowState == "BULLISH_ABSORPTION" || ofResult.OrderFlowState == "SPOOFING_TRAP")) isShort = false;
                if (isLong && ofResult.CumulativeVolumeDelta < -100) isLong = false;
                if (isShort && ofResult.CumulativeVolumeDelta > 100) isShort = false;

                // --- MODULE 5: Consensus ---
                var consensus = ConsensusEngine.EvaluateConsensus(
                    totalScore: totalScore,
                    scoreSign: Math.Sign(totalScore),
                    lgbmDirection: "NEUTRAL",
                    lgbmConfidence: 0.5,
                    lgbmAccuracy: 0.5,
                    rsiVal: taResult.rsiVal,
                    emaVal: taResult.emaVal,
                    isSubMinute: false,
                    wfWeightMultiplier: 1.0
                );

                if (consensus.FinalDirection == "NEUTRAL") 
                {
                    isLong = false;
                    isShort = false;
                }
                
                if (!isLong && !isShort) continue;
                if (taResult.atrVal == 0) continue;
                
                // --- MODULE 6: Trade Timeout Engine ---
                var timeoutResult = timeoutEngine.CalculateTimeout(
                    asset: "EURUSD",
                    timeframe: "1m",
                    atr: taResult.atrVal,
                    volRatio: taResult.volStrengthVal,
                    smc: smcResult
                );
                
                int expiryCandles = timeoutResult.TimeoutCandles;
                
                if (expiryDistribution.ContainsKey(expiryCandles))
                    expiryDistribution[expiryCandles]++;
                else
                    expiryDistribution[expiryCandles] = 1;

                double tpDistance = taResult.atrVal * 2.0;
                double slDistance = taResult.atrVal * 1.5;
                
                double tpPrice = isLong ? entryPrice + tpDistance : entryPrice - tpDistance;
                double slPrice = isLong ? entryPrice - slDistance : entryPrice + slDistance;

                bool resolved = false;
                bool won = false;
                double pipsGained = 0;
                int exitIndex = i;

                for (int future = i; future < i + expiryCandles && future < allCandles.Count; future++)
                {
                    exitIndex = future;
                    var futureCandle = allCandles[future];
                    if (isLong)
                    {
                        if (futureCandle.Low <= slPrice) { resolved = true; won = false; pipsGained = -slDistance; break; }
                        if (futureCandle.High >= tpPrice) { resolved = true; won = true; pipsGained = tpDistance; break; }
                    }
                    else
                    {
                        if (futureCandle.High >= slPrice) { resolved = true; won = false; pipsGained = -slDistance; break; }
                        if (futureCandle.Low <= tpPrice) { resolved = true; won = true; pipsGained = tpDistance; break; }
                    }
                }

                if (!resolved)
                {
                    tradesExpiredByTime++;
                    double exitPrice = closes[exitIndex];
                    pipsGained = isLong ? (exitPrice - entryPrice) : (entryPrice - exitPrice);
                    won = pipsGained > 0;
                }

                // Simulate M4
                if (won)
                {
                    currentLossStreak = 0;
                }
                else
                {
                    currentLossStreak++;
                    if (currentLossStreak >= 3)
                    {
                        cooloffUntilIndex = exitIndex + 15; 
                        currentLossStreak = 0; 
                    }
                }
                
                i = exitIndex;
                double pips = pipsGained * 10000;
                
                totalTrades++;
                currentBalance += pips;
                if (currentBalance > peakBalance) peakBalance = currentBalance;
                
                double drawdown = peakBalance - currentBalance;
                if (drawdown > maxDrawdown) maxDrawdown = drawdown;

                if (won) { totalWins++; grossProfitPips += pips; }
                else { totalLosses++; grossLossPips += Math.Abs(pips); }
            }
            
            double winRate = totalTrades > 0 ? (double)totalWins / totalTrades * 100.0 : 0;
            double profitFactor = grossLossPips > 0 ? grossProfitPips / grossLossPips : (grossProfitPips > 0 ? 999 : 0);
            
            Console.WriteLine($"[BACKTEST STATISTICS: M1-M6 (Trade Timeout)]");
            Console.WriteLine($"Total Trades Executed : {totalTrades}");
            Console.WriteLine($"Win Rate              : {winRate:F2}% ({totalWins} Wins / {totalLosses} Losses)");
            Console.WriteLine($"Profit Factor         : {profitFactor:F2}");
            Console.WriteLine($"Net PnL               : {currentBalance:F1} Pips");
            Console.WriteLine($"Max Drawdown          : {maxDrawdown:F1} Pips");
            Console.WriteLine($"Trades Force-Closed   : {tradesExpiredByTime} (Time Limit Exceeded)");
            Console.WriteLine($"");
            Console.WriteLine($"[M6 Timeout Distributions]");
            foreach (var kvp in expiryDistribution)
            {
                Console.WriteLine($"  {kvp.Key} candles: {kvp.Value} trades");
            }
        }
    }
}
