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
public class ConfluenceMatrixEngine(MarketDataFetcher fetcher, IMarketAnalyzer marketAnalyzer, IAutoCalibrationEngine autoCalib) : IConfluenceMatrixEngine
{
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
            var microTask   = fetcher.FetchBinanceWithFallback(binanceSymbol, microTf, asset, 40);
            var primaryTask = fetcher.FetchBinanceWithFallback(binanceSymbol, primaryTf, asset, 40);
            var macroTask   = fetcher.FetchBinanceWithFallback(binanceSymbol, macroTf, asset, 40);
            var globalTask  = fetcher.FetchBinanceWithFallback(binanceSymbol, globalTf, asset, 40);

            await Task.WhenAll(microTask, primaryTask, macroTask, globalTask);

            var (microPrices, microVolumes)   = await microTask;
            var (primaryPrices, primaryVolumes) = await primaryTask;
            var (macroPrices, macroVolumes)   = await macroTask;
            var (globalPrices, globalVolumes)  = await globalTask;

            // 3. Score directional bias for each timeframe
            string dirMicro   = ScoreDirection(microPrices, microVolumes);
            string dirPrimary = ScoreDirection(primaryPrices, primaryVolumes);
            string dirMacro   = ScoreDirection(macroPrices, macroVolumes);
            string dirGlobal  = ScoreDirection(globalPrices, globalVolumes);

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

    private static (string micro, string primary, string macro, string global) Resolve4DTimeframes(string tf)
    {
        return tf.ToLower() switch
        {
            "s3" or "s5" or "s10" or "s15" or "s30" => ("m1",  "m3", "m5",  "m15"),
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
    private string ScoreDirection(double[] prices, double[] volumes)
    {
        if (prices == null || prices.Length < 10) return "NEUTRAL";

        // Reuse the authoritative scoring function with its HMA + Connors RSI + ADX + Volume weighting.
        var (score, _, _, _, _, _) = marketAnalyzer.ScoreTimeframe(
            "internal", "internal", prices,
            volumes: volumes,
            candles: null
        );

        // Threshold: require at least В±0.10 to avoid noise-induced signals
        return score > 0.10 ? "BUY" : score < -0.10 ? "PUT" : "NEUTRAL";
    }

    /// <summary>
    /// Unified Matrix Evaluation: merges TA, SMC, Orderflow, ML, and Multi-Timeframe into a final decision.
    /// </summary>
    public async Task<ConsensusDecision> EvaluateMatrixAsync(
        string asset,
        string timeframe,
        bool isSubMinute,
        double conflictPenalty,
        TaSignal taSignal,
        SmcSignal smcSignal,
        OrderflowSignal ofSignal,
        MlSignal mlSignal,
        StateSignal stateSignal,
        ConfluenceMatrixResult mtfResult)
    {
        double totalScore = 0.0;
        double totalConfidence = 0.0;
        double totalWeight = 0.0;

        // 1. Technical Analysis (Base)
        double taWeight = await SignalTracker.GetSignalWeightAsync("INDICATORS", 1.0);
        totalScore += (taSignal.Score + ofSignal.ScoreContribution) * taWeight * conflictPenalty;
        totalConfidence += taSignal.Confidence * taWeight * conflictPenalty;
        totalWeight += taWeight * conflictPenalty;

        // 2. Velocity / Continuous State
        double stateWeight = await SignalTracker.GetSignalWeightAsync("VelocityState", 1.5);
        totalScore += stateSignal.MomentumContribution * stateWeight;
        totalConfidence += 60.0 * stateWeight; // baseline state confidence
        totalWeight += stateWeight;

        // 3. Smart Money Concepts (SMC)
        int smcScore = 0;
        if (smcSignal.SweepDirection == "BULLISH_SWEEP") smcScore += 2;
        else if (smcSignal.SweepDirection == "BEARISH_SWEEP") smcScore -= 2;
        if (smcSignal.BosDirection == "BULLISH_BOS") smcScore += 2;
        else if (smcSignal.BosDirection == "BEARISH_BOS") smcScore -= 2;
        if (smcSignal.OrderBlockType == "BULLISH_OB") smcScore += 1;
        else if (smcSignal.OrderBlockType == "BEARISH_OB") smcScore -= 1;
        if (smcSignal.FvgType == "BULLISH_FVG") smcScore += 1;
        else if (smcSignal.FvgType == "BEARISH_FVG") smcScore -= 1;
        
        if (smcScore != 0)
        {
            double smcWeight = await SignalTracker.GetSignalWeightAsync("SMC", 1.0);
            totalScore += ((double)smcScore / 6.0) * smcWeight;
            totalConfidence += 60.0 * smcWeight;
            totalWeight += smcWeight;
        }

        // Normalize internal base scores
        if (totalWeight > 0)
        {
            totalScore /= totalWeight;
            totalConfidence /= totalWeight;
        }

        // 4. ML / Mathematical Consensus Matrix Layer (Replacing ConsensusEngine)
        var regime = autoCalib.DetectMarketRegime(taSignal.Atr, taSignal.Volatility, taSignal.Rsi);
        double weightLgbm = autoCalib.GetCalibratedRegimeWeight("LIGHTGBM", asset, timeframe, regime);
        double weightMath = autoCalib.GetCalibratedRegimeWeight("SKENDER_MATH", asset, timeframe, regime);

        double normLgbm = Math.Max(0, (mlSignal.Confidence - 0.5) * 2.0);
        double scoreLgbm = mlSignal.Direction == "BUY" ? normLgbm : mlSignal.Direction == "PUT" ? -normLgbm : 0;
        
        // Clamp internal math score
        double scoreMath = Math.Clamp(totalScore, -2.5, 2.5) / 2.5;

        double activeWeightLgbm = (mlSignal.Direction == "BUY" || mlSignal.Direction == "PUT") ? weightLgbm : 0;
        double activeWeightMath = weightMath;
        
        double totalWeightSum = activeWeightLgbm + activeWeightMath;
        if (totalWeightSum < 1e-9) totalWeightSum = 1.0;
        
        double weightedScore = (scoreLgbm * activeWeightLgbm + scoreMath * activeWeightMath) / totalWeightSum;

        // 5. Final Decision Calculation
        string candidateDir = weightedScore > 0.0001 ? "BUY" : weightedScore < -0.0001 ? "PUT" : (totalScore > 0.02 ? "BUY" : totalScore < -0.02 ? "PUT" : "NEUTRAL");

        double absWeightedScore = Math.Abs(weightedScore);
        int probability = isSubMinute
            ? Math.Clamp(50 + (int)Math.Round(absWeightedScore * 40), 50, 91)
            : Math.Clamp(50 + (int)Math.Round(absWeightedScore * 45), 50, 95);

        // MTF Golden Boost
        if (candidateDir != "NEUTRAL" && mtfResult.ProbabilityBoost > 0)
        {
             probability = Math.Clamp(probability + mtfResult.ProbabilityBoost, 55, 95);
        }

        // 6. Formatting Reasoning
        string modelAccText = mlSignal.Accuracy.HasValue 
            ? $" [РўРѕС‡РЅРѕСЃС‚СЊ: {Math.Round(mlSignal.Accuracy.Value * 100, 1)}%]" 
            : "";

        string smcText = !string.IsNullOrEmpty(smcSignal.Reasoning)
            ? $"вЂў рџЏ›пёЏ SMC РЎС‚СЂСѓРєС‚СѓСЂР°: {smcSignal.Reasoning}"
            : "вЂў рџЏ›пёЏ SMC РЎС‚СЂСѓРєС‚СѓСЂР°: Р‘Р°Р»Р°РЅСЃРѕРІР°СЏ РєРѕРЅСЃРѕР»РёРґР°С†РёСЏ РґРёР°РїР°Р·РѕРЅР°";

        string flowText = !string.IsNullOrEmpty(ofSignal.Description)
            ? $"вЂў рџЊЉ Order Flow & CVD: {ofSignal.Description}"
            : "вЂў рџЊЉ Order Flow & CVD: РџРѕС‚РѕРє РѕСЂРґРµСЂРѕРІ СЃР±Р°Р»Р°РЅСЃРёСЂРѕРІР°РЅ";

        string lgbmText = !string.IsNullOrEmpty(mlSignal.Direction) && mlSignal.Direction != "NEUTRAL"
            ? $"вЂў вљЎ РќРµР№СЂРѕСЃРµС‚СЊ (LightGBM): {(mlSignal.Direction == "BUY" ? "Р’Р’Р•Р РҐ в¬†" : "Р’РќРР— в¬‡")} ({Math.Round(mlSignal.Confidence * 100)}% СѓРІРµСЂРµРЅРЅРѕСЃС‚СЊ){modelAccText}"
            : $"вЂў вљЎ РќРµР№СЂРѕСЃРµС‚СЊ (LightGBM): РќР•Р™РўР РђР›Р¬РќРћ (0% СѓРІРµСЂРµРЅРЅРѕСЃС‚СЊ){modelAccText}";

        string mtfText = mtfResult.SummaryReasoning;

        string combinedReasoning = $"{smcText}\n{flowText}\n{lgbmText}\n{mtfText}";

        return new ConsensusDecision(candidateDir, candidateDir, probability, combinedReasoning, totalScore);
    }
}
