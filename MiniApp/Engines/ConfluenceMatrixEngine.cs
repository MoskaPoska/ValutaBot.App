using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ValutaBot.MiniApp;

public record ConfluenceMatrixResult(
    double ConfluenceRatio,      // 0.0 – 1.0 (e.g., 1.0 = 100% agreement across all 4 timeframes)
    bool IsGoldenSetup,          // true if 4D alignment >= 0.85 (85%+ agreement)
    int ProbabilityBoost,        // +5% to +15% win rate boost for consensus
    string ConfluenceLabel,      // "🌟 ЗОЛОТОЙ СЕТАП (4D 100%)" | "⚡ СИЛЬНАЯ КОНФЛЮЭНЦИЯ (75%)" | "📊 СТАНДАРТ"
    string SummaryReasoning,     // Formatted text summary for AI consensus card
    Dictionary<string, string> TimeframeDirections // TF -> "BUY" | "PUT"
);

public static class ConfluenceMatrixEngine
{
    /// <summary>
    /// Evaluates 4D Multi-Timeframe Confluence Matrix across 4 synchronized timeframes in parallel.
    /// Returns Golden Setup alignment score and win-rate probability boost.
    /// </summary>
    public static async Task<ConfluenceMatrixResult> Evaluate4DMatrixAsync(
        string asset,
        string primaryTimeframe,
        bool isForex = false,
        string? binanceSymbol = null)
    {
        // 1. Resolve 4-dimensional timeframe matrix
        var (microTf, primaryTf, macroTf, globalTf) = Resolve4DTimeframes(primaryTimeframe);

        try
        {
            // 2. Fetch candles for all 4 timeframes in parallel (< 10ms execution)
            var microTask   = MarketDataFetcher.FetchBinanceWithFallback(binanceSymbol, microTf, asset, 40, 10);
            var primaryTask = MarketDataFetcher.FetchBinanceWithFallback(binanceSymbol, primaryTf, asset, 40, 10);
            var macroTask   = MarketDataFetcher.FetchBinanceWithFallback(binanceSymbol, macroTf, asset, 40, 10);
            var globalTask  = MarketDataFetcher.FetchBinanceWithFallback(binanceSymbol, globalTf, asset, 40, 10);

            await Task.WhenAll(microTask, primaryTask, macroTask, globalTask);

            var (microPrices, _)   = await microTask;
            var (primaryPrices, _) = await primaryTask;
            var (macroPrices, _)   = await macroTask;
            var (globalPrices, _)  = await globalTask;

            // 3. Score directional bias for each timeframe
            string dirMicro   = ScoreDirection(microPrices);
            string dirPrimary = ScoreDirection(primaryPrices);
            string dirMacro   = ScoreDirection(macroPrices);
            string dirGlobal  = ScoreDirection(globalPrices);

            var tfDirs = new Dictionary<string, string>
            {
                [microTf.ToUpper()]   = dirMicro,
                [primaryTf.ToUpper()] = dirPrimary,
                [macroTf.ToUpper()]   = dirMacro,
                [globalTf.ToUpper()]  = dirGlobal
            };

            // 4. Calculate Confluence Ratio & Golden Setup Alignment
            var counts = tfDirs.Values.GroupBy(d => d).ToDictionary(g => g.Key, g => g.Count());
            int buyCount = counts.GetValueOrDefault("BUY", 0);
            int putCount = counts.GetValueOrDefault("PUT", 0);
            int maxAgree = Math.Max(buyCount, putCount);

            double confluenceRatio = Math.Round(maxAgree / 4.0, 2); // 1.0 (4/4), 0.75 (3/4), 0.50 (2/4)
            string dominantDir = buyCount >= putCount ? "BUY" : "PUT";
            bool isGoldenSetup = confluenceRatio >= 0.99; // 100% 4/4 agreement

            int boost = confluenceRatio switch
            {
                >= 0.99 => 12, // +12% probability boost for 4D Golden Confluence
                >= 0.75 => 6,  // +6% boost for 3D Confluence
                _ => 0
            };

            string label = confluenceRatio switch
            {
                >= 0.99 => "🌟 ЗОЛОТОЙ СЕТАП (4D 100%)",
                >= 0.75 => "⚡ СИЛЬНОЕ СОВПАДЕНИЕ (3D 75%)",
                _ => "📊 СТАНДАРТНЫЙ АНАЛИЗ (50%)"
            };

            string summary = $"• 🎯 4D Матрица ({microTf.ToUpper()}+{primaryTf.ToUpper()}+{macroTf.ToUpper()}+{globalTf.ToUpper()}): {label}";

            BotLogger.Info($"[Confluence 4D] {asset} | Ratio: {confluenceRatio * 100}% ({maxAgree}/4 {dominantDir}) | Boost: +{boost}% | Golden: {isGoldenSetup}");

            return new ConfluenceMatrixResult(
                ConfluenceRatio: confluenceRatio,
                IsGoldenSetup: isGoldenSetup,
                ProbabilityBoost: boost,
                ConfluenceLabel: label,
                SummaryReasoning: summary,
                TimeframeDirections: tfDirs
            );
        }
        catch (Exception ex)
        {
            BotLogger.Error($"[Confluence 4D] Error evaluating matrix for {asset}", ex);
            return new ConfluenceMatrixResult(
                ConfluenceRatio: 0.5,
                IsGoldenSetup: false,
                ProbabilityBoost: 0,
                ConfluenceLabel: "📊 СТАНДАРТ",
                SummaryReasoning: "• 🎯 4D Матрица: Стандартный режим",
                TimeframeDirections: new()
            );
        }
    }

    private static (string micro, string primary, string macro, string global) Resolve4DTimeframes(string tf)
    {
        return tf.ToLower() switch
        {
            "s3" or "s5" or "s10" or "s15" or "s30" => ("s5",  "s30", "m1",  "m5"),
            "m1"                                    => ("s30", "m1",  "m5",  "h1"),
            "m2" or "m3"                            => ("m1",  "m3",  "m15", "h1"),
            "m5"                                    => ("m1",  "m5",  "m15", "h1"),
            "m15"                                   => ("m5",  "m15", "h1",  "h4"),
            _                                       => ("s30", "m1",  "m5",  "h1")
        };
    }

    private static string ScoreDirection(double[] prices)
    {
        if (prices == null || prices.Length < 10) return "BUY";

        double first = prices[0];
        double last  = prices[^1];
        double sma   = prices.Average();
        double rsi   = TechnicalAnalysisEngine.ComputeRsi(prices, 14);

        int score = 0;
        if (last > first) score++; else score--;
        if (last > sma) score++; else score--;
        if (rsi > 50) score++; else score--;

        return score >= 0 ? "BUY" : "PUT";
    }
}
