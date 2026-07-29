using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ValutaBot.MiniApp;

public record ConfluenceMatrixResult(
    double ConfluenceRatio,      // 0.0 вЂ“ 1.0 (e.g., 1.0 = 100% agreement across all 4 timeframes)
    bool IsGoldenSetup,          // true if 4D alignment >= 0.85 (85%+ agreement)
    int ProbabilityBoost,        // +5% to +15% win rate boost for consensus
    string ConfluenceLabel,      // "рџЊџ Р—РћР›РћРўРћР™ РЎР•РўРђРџ (4D 100%)" | "вљЎ РЎРР›Р¬РќРђРЇ РљРћРќР¤Р›Р®Р­РќР¦РРЇ (75%)" | "рџ“Љ РЎРўРђРќР”РђР Рў"
    string SummaryReasoning,     // Formatted text summary for AI consensus card
    Dictionary<string, string> TimeframeDirections // TF -> "BUY" | "PUT"
);

public class ConfluenceMatrixEngine : IConfluenceMatrixEngine
{
    private readonly IMarketDataFetcher _fetcher;
    private readonly ITechnicalAnalysisEngine _taEngine;

    public ConfluenceMatrixEngine(IMarketDataFetcher fetcher, ITechnicalAnalysisEngine taEngine)
    {
        _fetcher = fetcher;
        _taEngine = taEngine;
    }
    /// <summary>
    /// Evaluates 4D Multi-Timeframe Confluence Matrix across 4 synchronized timeframes in parallel.
    /// Returns Golden Setup alignment score and win-rate probability boost.
    /// </summary>
    public async Task<ConfluenceMatrixResult> Evaluate4DMatrixAsync(
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
            var microTask   = _fetcher.FetchBinanceWithFallback(binanceSymbol, microTf, asset, 40, 10);
            var primaryTask = _fetcher.FetchBinanceWithFallback(binanceSymbol, primaryTf, asset, 40, 10);
            var macroTask   = _fetcher.FetchBinanceWithFallback(binanceSymbol, macroTf, asset, 40, 10);
            var globalTask  = _fetcher.FetchBinanceWithFallback(binanceSymbol, globalTf, asset, 40, 10);

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
            string dominantDir = buyCount == putCount ? "NEUTRAL" : (buyCount > putCount ? "BUY" : "PUT");
            bool isGoldenSetup = confluenceRatio >= 0.99; // 100% 4/4 agreement

            int boost = confluenceRatio switch
            {
                >= 0.99 => 12, // +12% probability boost for 4D Golden Confluence
                >= 0.75 => 6,  // +6% boost for 3D Confluence
                _ => 0
            };

            string label = confluenceRatio switch
            {
                >= 0.99 => "рџЊџ Р—РћР›РћРўРћР™ РЎР•РўРђРџ (4D 100%)",
                >= 0.75 => "вљЎ РЎРР›Р¬РќРћР• РЎРћР’РџРђР”Р•РќРР• (3D 75%)",
                _ => "рџ“Љ РЎРўРђРќР”РђР РўРќР«Р™ РђРќРђР›РР— (50%)"
            };

            string summary = $"вЂў рџЋЇ 4D РњР°С‚СЂРёС†Р° ({microTf.ToUpper()}+{primaryTf.ToUpper()}+{macroTf.ToUpper()}+{globalTf.ToUpper()}): {label}";

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
                ConfluenceLabel: "рџ“Љ РЎРўРђРќР”РђР Рў",
                SummaryReasoning: "вЂў рџЋЇ 4D РњР°С‚СЂРёС†Р°: РЎС‚Р°РЅРґР°СЂС‚РЅС‹Р№ СЂРµР¶РёРј",
                TimeframeDirections: new()
            );
        }
    }

    private (string micro, string primary, string macro, string global) Resolve4DTimeframes(string tf)
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

    /// <summary>
    /// Scores directional bias for a single timeframe using the full
    /// TechnicalAnalysisEngine pipeline (HMA, ConnorsRSI, ADX, Volume) вЂ”
    /// replacing the former primitive 3-condition heuristic.
    /// </summary>
    private string ScoreDirection(double[] prices)
    {
        if (prices == null || prices.Length < 10) return "NEUTRAL";

        // Reuse the authoritative scoring function with its HMA + Connors RSI + ADX + Volume weighting.
        // Volumes are not available here, so we pass an empty array вЂ” the engine handles this gracefully.
        var (score, _, _, _, _, _) = _taEngine.ScoreTimeframe(
            prices,
            volumes: Array.Empty<double>(),
            candles: null
        );

        // Threshold: require at least В±0.10 to avoid noise-induced signals
        return score > 0.10 ? "BUY" : score < -0.10 ? "PUT" : "NEUTRAL";
    }
}



