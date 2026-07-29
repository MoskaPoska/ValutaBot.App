using System;
using System.Linq;
using System.Threading.Tasks;

namespace ValutaBot.MiniApp;

public class BacktestEngine : IBacktestEngine
{
    private readonly MarketDataFetcher _fetcher;
    private readonly ITechnicalAnalysisEngine _taEngine;

    public BacktestEngine(MarketDataFetcher fetcher, ITechnicalAnalysisEngine taEngine)
    {
        _fetcher = fetcher;
        _taEngine = taEngine;
    }
    public async Task RunBacktestAsync(string asset, string timeframe, int limit = 1000)
    {
        Console.WriteLine($"[Backtest] Starting historical simulation for {asset} on {timeframe}...");
        string interval = _fetcher.IntervalMap(timeframe);
        
        // Fetch historical data
        var (prices, volumes) = await _fetcher.FetchBinanceCandles(asset, interval, limit);
        if (prices.Length < 100)
        {
            Console.WriteLine("[Backtest] Not enough data fetched.");
            return;
        }

        var ohlcAll = _fetcher.GetOhlcCandles($"{asset}_{interval}");
        if (ohlcAll == null || ohlcAll.Length != prices.Length)
        {
            Console.WriteLine("[Backtest] OHLC Cache missing or mismatched length. Cannot run backtest without real OHLC data.");
            return;
        }

        Console.WriteLine($"[Backtest] Fetched {prices.Length} candles.");

        int wins = 0;
        int losses = 0;
        int neutral = 0;

        int expiryCandles = _fetcher.GetExpiryCandles(timeframe);

        for (int i = 50; i < prices.Length - expiryCandles; i++)
        {
            // Sliding window: up to index i
            var windowPrices = prices[..i];
            var windowVolumes = volumes[..i];
            
            // Use real historical OHLC window without Look-Ahead Bias
            var ohlcWindow = ohlcAll[..i];

            var gatekeeper = _taEngine.ValidateMarketGatekeeper(windowPrices, ohlcWindow);
            if (!gatekeeper.IsTradeable) continue;

            var ta = _taEngine.ScoreTimeframe(windowPrices, windowVolumes, ohlcWindow);
            var smc = SmcEngine.AnalyzeSmcStructure(ohlcWindow, windowPrices[^1]);
            
            int smcScore = 0;
            if (smc.SweepDirection == "BULLISH_SWEEP") smcScore += 2;
            else if (smc.SweepDirection == "BEARISH_SWEEP") smcScore -= 2;
            if (smc.BosDirection == "BULLISH_BOS") smcScore += 2;
            else if (smc.BosDirection == "BEARISH_BOS") smcScore -= 2;
            if (smc.OrderBlockType == "BULLISH_OB") smcScore += 1;
            else if (smc.OrderBlockType == "BEARISH_OB") smcScore -= 1;
            if (smc.FvgType == "BULLISH_FVG") smcScore += 1;
            else if (smc.FvgType == "BEARISH_FVG") smcScore -= 1;
            
            string smcDir = smcScore > 0 ? "BUY" : smcScore < 0 ? "PUT" : "NEUTRAL";
            
            // Add SMC score to TA score for a holistic Core score in backtesting
            double combinedScore = ta.score + (smcScore * 0.25);

            // Mock ML as NEUTRAL for deterministic core testing
            var consensus = ConsensusEngine.EvaluateConsensus(
                combinedScore,
                combinedScore > 0 ? 1 : combinedScore < 0 ? -1 : 0,
                "NEUTRAL", 50, "",
                // Pass 0.5 (normalized [0..1]) — not 50 (which was incorrectly treated as int,
                // making normLgbm = ((50/100.0) - 0.5) * 2 = 0 → zero ML weight in backtest)
                "NEUTRAL", 0.5, null,
                "NEUTRAL", 50.0,
                "NEUTRAL", 0.5,
                ta.rsiVal, ta.emaVal,
                timeframe.StartsWith("s"),
                asset, timeframe,
                20.0, 1.0,
                smc.SummaryReasoning,
                "", "Backtest"
            );

            if (consensus.FinalDirection == "BUY" || consensus.FinalDirection == "PUT")
            {
                if (consensus.Probability >= 70)
                {
                    double entryPrice = windowPrices[^1];
                    double exitPrice = prices[i + expiryCandles - 1];
                    
                    bool isWin = false;
                    if (consensus.FinalDirection == "BUY") isWin = exitPrice > entryPrice;
                    if (consensus.FinalDirection == "PUT") isWin = exitPrice < entryPrice;

                    if (isWin) wins++;
                    else losses++;
                }
                else
                {
                    neutral++;
                }
            }
        }

        int totalSignals = wins + losses;
        double winRate = totalSignals > 0 ? (double)wins / totalSignals * 100 : 0;

        Console.WriteLine("==================================================");
        Console.WriteLine("                BACKTEST REPORT                   ");
        Console.WriteLine("==================================================");
        Console.WriteLine($"Asset:      {asset}");
        Console.WriteLine($"Timeframe:  {timeframe} (Expiry: {expiryCandles} candles)");
        Console.WriteLine($"Candles:    {prices.Length} (Simulating {prices.Length - 50} steps)");
        Console.WriteLine("--------------------------------------------------");
        Console.WriteLine($"Total Signals:  {totalSignals}");
        Console.WriteLine($"Wins:           {wins}");
        Console.WriteLine($"Losses:         {losses}");
        Console.WriteLine($"Skipped (Low %):{neutral}");
        Console.WriteLine($"Win-Rate (Core):{winRate:F2}%");
        Console.WriteLine("==================================================");
    }
}




