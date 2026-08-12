using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ValutaBot.MiniApp;

public record ConfluenceMatrixResult(
    double ConfluenceRatio,
    bool IsGoldenSetup,
    int ProbabilityBoost,
    string ConfluenceLabel,
    string SummaryReasoning,
    Dictionary<string, string> TimeframeDirections,
    string DominantDirection              // "BUY" | "PUT" | "NEUTRAL"
);

public class ConfluenceMatrixEngine(
    MarketDataFetcher fetcher,
    IMarketAnalyzer marketAnalyzer,
    Microsoft.Extensions.Options.IOptions<TradingBotSettings>? options = null) : IConfluenceMatrixEngine
{
    // в”Ђв”Ђ 4D Matrix в”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђ

    public async Task<ConfluenceMatrixResult> Evaluate4DMatrixAsync(
        string asset,
        string primaryTimeframe,
        bool isForex = false,
        string? binanceSymbol = null)
    {
        var (microTf, primaryTf, macroTf) = Resolve3DTimeframes(primaryTimeframe);

        try
        {
            var microTask   = fetcher.FetchBinanceWithFallback(binanceSymbol, microTf,   asset, 40);
            var primaryTask = fetcher.FetchBinanceWithFallback(binanceSymbol, primaryTf, asset, 40);
            var macroTask   = fetcher.FetchBinanceWithFallback(binanceSymbol, macroTf,   asset, 40);

            await Task.WhenAll(microTask, primaryTask, macroTask);

            var (microPrices,   microVolumes)   = await microTask;
            var (primaryPrices, primaryVolumes) = await primaryTask;
            var (macroPrices,   macroVolumes)   = await macroTask;

            string dirMicro   = ScoreDirection(microPrices,   microVolumes);
            string dirPrimary = ScoreDirection(primaryPrices, primaryVolumes);
            string dirMacro   = ScoreDirection(macroPrices,   macroVolumes);

            var tfDirs = new Dictionary<string, string>
            {
                [microTf.ToUpper()]   = dirMicro,
                [primaryTf.ToUpper()] = dirPrimary,
                [macroTf.ToUpper()]   = dirMacro,
            };

            var counts    = tfDirs.Values.GroupBy(d => d).ToDictionary(g => g.Key, g => g.Count());
            int buyCount  = counts.GetValueOrDefault("BUY", 0);
            int putCount  = counts.GetValueOrDefault("PUT", 0);
            int maxAgree  = Math.Max(buyCount, putCount);

            double confluenceRatio = Math.Round(maxAgree / 3.0, 2);
            string dominantDir     = buyCount == putCount ? "NEUTRAL"
                                   : buyCount > putCount ? "BUY" : "PUT";
            bool isGoldenSetup     = confluenceRatio >= 0.99;

            int boost = confluenceRatio switch
            {
                >= 0.99 => 12,
                >= 0.65 => 6,
                _       => 0
            };

            string label = confluenceRatio switch
            {
                >= 0.99 => "\u2b50 GOLDEN SETUP (3D 100%)",
                >= 0.65 => "\u26a1 STRONG CONFLUENCE (2D 67%)",
                _       => "\ud83d\udcca STANDARD ANALYSIS (33%)"
            };

            string summary = $"\u2022 \U0001f3af 3D Matrix ({microTf.ToUpper()}+{primaryTf.ToUpper()}+{macroTf.ToUpper()}): {label}";

            BotLogger.Info($"[Confluence 3D] {asset} | Ratio: {confluenceRatio * 100}% ({maxAgree}/3 {dominantDir}) | Boost: +{boost}% | Golden: {isGoldenSetup}");

            return new ConfluenceMatrixResult(
                ConfluenceRatio:      confluenceRatio,
                IsGoldenSetup:        isGoldenSetup,
                ProbabilityBoost:     boost,
                ConfluenceLabel:      label,
                SummaryReasoning:     summary,
                TimeframeDirections:  tfDirs,
                DominantDirection:    dominantDir
            );
        }
        catch (Exception ex)
        {
            BotLogger.Error($"[Confluence 3D] Error evaluating matrix for {asset}", ex);
            throw new Exception($"OTKAZ API: 3D Matrix error for {asset}. {ex.Message}");
        }
    }

        private static (string micro, string primary, string macro)
        Resolve3DTimeframes(string tf) =>
        tf.ToLower() switch
        {
            "s3" or "s5" or "s10" or "s15" or "s30" => ("m1",  "m3",  "m5"),
            "m1"                                     => ("s30", "m1",  "m5"),
            "m2" or "m3"                             => ("m1",  "m3",  "m15"),
            "m5"                                     => ("m1",  "m5",  "m15"),
            "m15"                                    => ("m5",  "m15", "h1"),
            _                                        => ("s30", "m1",  "m5")
        };

    /// <summary>
    /// Scores directional bias for a single timeframe using the full
    /// TechnicalAnalysisEngine pipeline (HMA, ConnorsRSI, ADX, Volume).
    ///
    /// FIX: Previously passed candles=null to ScoreTimeframe, which caused
    /// candles.Length == 0 &lt; 14 в†’ always return score=0.0 в†’ always "NEUTRAL".
    /// Now constructs a real OhlcCandle[] from price/volume arrays.
    /// </summary>
    private string ScoreDirection(double[] prices, double[] volumes)
    {
        if (prices == null || prices.Length < 10) 
        {
            throw new Exception($"РћРўРљРђР— API: РџРѕР»СѓС‡РµРЅРѕ {(prices == null ? 0 : prices.Length)} СЃРІРµС‡РµР№ РґР»СЏ РјР°С‚СЂРёС†С‹ (РЅСѓР¶РЅРѕ РјРёРЅ 10).");
        }

        double avgDiff = 0;
        if (prices.Length > 1) {
            for (int k = 1; k < prices.Length; k++) avgDiff += Math.Abs(prices[k] - prices[k - 1]);
            avgDiff /= (prices.Length - 1);
        }
        if (avgDiff == 0) avgDiff = prices[0] * 0.0001;

        // ArrayPool: РІРјРµСЃС‚Рѕ new OhlcCandle[n] (4 Р°Р»Р»РѕРєР°С†РёРё РЅР° Р·Р°РїСЂРѕСЃ) Р±РµСЂС‘Рј Р±СѓС„РµСЂ РёР· РїСѓР»Р°.
        var candles = ArrayPool<MiniAppController.OhlcCandle>.Shared.Rent(prices.Length);
        try
        {
            for (int i = 0; i < prices.Length; i++)
            {
                double v = volumes != null && i < volumes.Length ? volumes[i] : 1.0;
                double open = i > 0 ? prices[i - 1] : prices[i];
                double close = prices[i];
                double high = Math.Max(open, close) + avgDiff * 0.5;
                double low = Math.Min(open, close) - avgDiff * 0.5;

                // OhlcCandle is a positional record: (Open, High, Low, Close, Volume, Timestamp)
                candles[i] = new MiniAppController.OhlcCandle(
                    open, high, low, close,
                    v,
                    DateTime.UtcNow.AddMinutes(i - prices.Length)
                );
            }

            var (score, _, _, _, _, _) = marketAnalyzer.ScoreTimeframe(
                "internal", "internal", prices,
                volumes: volumes,
                candles: candles.AsSpan(0, prices.Length)
            );

            // РџРѕСЂРѕРі РїРѕРґРЅСЏС‚ СЃ В±0.10 РґРѕ В±0.20: РїСЂРё С€РєР°Р»Рµ [-1, +1] РїСЂРµР¶РЅРёР№ РїРѕСЂРѕРі 0.10
            // РєР»Р°СЃСЃРёС„РёС†РёСЂРѕРІР°Р» ~80% С€СѓРјРѕРІРѕРіРѕ СЂС‹РЅРєР° РєР°Рє РЅР°РїСЂР°РІР»РµРЅРЅС‹Р№ СЃРёРіРЅР°Р» (BUY/PUT).
            return score > 0.20 ? "BUY" : score < -0.20 ? "PUT" : "NEUTRAL";
        }
        finally
        {
            ArrayPool<MiniAppController.OhlcCandle>.Shared.Return(candles);
        }
    }

    // в”Ђв”Ђ Unified Matrix Evaluation в”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђ

    /// <summary>
    /// Merges TA, SMC, Orderflow, ML, and Multi-Timeframe into a final decision.
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
        double totalScore      = 0.0;
        double totalConfidence = 0.0;
        double totalWeight     = 0.0;

        // 1. Technical Analysis (Lagging вЂ” СЂРѕР»СЊ С„РѕРЅРѕРІРѕРіРѕ С„РёР»СЊС‚СЂР°, РІРµСЃ СЃРЅРёР¶РµРЅ РґРѕ 0.5)
        double taWeight  = await SignalTracker.GetSignalWeightAsync("INDICATORS", 0.5);
        totalScore      += taSignal.Score * taWeight;
        totalConfidence += taSignal.Confidence * taWeight;
        totalWeight     += taWeight;

        // 1b. Order Flow (Leading вЂ” РІС‹РґРµР»РµРЅ РІ РѕС‚РґРµР»СЊРЅС‹Р№ РєРѕРјРїРѕРЅРµРЅС‚, РїСЂРёРѕСЂРёС‚РµС‚РЅС‹Р№ СЃРёРіРЅР°Р» РґР»СЏ РѕРїС†РёРѕРЅРѕРІ)
        double ofWeight  = await SignalTracker.GetSignalWeightAsync("ORDERFLOW", 1.8);
        totalScore      += ofSignal.ScoreContribution * ofWeight;
        totalConfidence += 65.0 * ofWeight;
        totalWeight     += ofWeight;

        // 2. Velocity / Continuous State (Leading вЂ” РјРёРєСЂРѕ-СѓСЃРєРѕСЂРµРЅРёРµ С†РµРЅС‹, РїРѕРІС‹С€РµРЅ РґРѕ 2.0)
        double stateWeight  = await SignalTracker.GetSignalWeightAsync("VelocityState", 2.0);
        totalScore         += stateSignal.MomentumContribution * stateWeight;
        totalConfidence    += 60.0 * stateWeight;
        totalWeight        += stateWeight;

        // 3. Smart Money Concepts (SMC) - Adaptive Regime Switching (Level 3 Fix)
        double smcTrendScore = 0.0;
        double smcReversionScore = 0.0;

        // Reversion / Range boundaries
        if (smcSignal.SweepDirection == "BULLISH_SWEEP") smcReversionScore += 2.0;
        else if (smcSignal.SweepDirection == "BEARISH_SWEEP") smcReversionScore -= 2.0;

        // Trend / Breakouts
        if (smcSignal.BosDirection == "BULLISH_BOS") smcTrendScore += 2.0;
        else if (smcSignal.BosDirection == "BEARISH_BOS") smcTrendScore -= 2.0;
        if (smcSignal.OrderBlockType == "BULLISH_OB") smcTrendScore += 1.0;
        else if (smcSignal.OrderBlockType == "BEARISH_OB") smcTrendScore -= 1.0;
        if (smcSignal.FvgType == "BULLISH_FVG") smcTrendScore += 1.0;
        else if (smcSignal.FvgType == "BEARISH_FVG") smcTrendScore -= 1.0;

        double trendWeight = 1.0;
        double reversionWeight = 1.0;

        if (taSignal.Adx < 20.0)
        {
            // Choppy / Flat Market: Nerf BOS, Boost Sweeps
            trendWeight = 0.0;
            reversionWeight = 2.0;
        }
        else if (taSignal.Adx > 25.0)
        {
            // Trending Market: Boost BOS, Nerf Sweeps
            trendWeight = 1.5;
            reversionWeight = 0.5;
        }

        double finalSmcScore = (smcTrendScore * trendWeight) + (smcReversionScore * reversionWeight);

        if (Math.Abs(finalSmcScore) > 0.1)
        {
            double smcWeight  = await SignalTracker.GetSignalWeightAsync("SMC", 1.5);
            totalScore       += (finalSmcScore / 6.0) * smcWeight;
            totalConfidence  += 60.0 * smcWeight;
            totalWeight      += smcWeight;
        }

        // Normalize internal base scores
        if (totalWeight > 0)
        {
            totalScore      /= totalWeight;
            totalConfidence /= totalWeight;
        }

        // Apply conflict penalty globally to the normalized score
        totalScore *= conflictPenalty;

        // 4. ML / Mathematical Consensus Matrix Layer (META-LABELING OVERRIDE)
        double scoreMath = Math.Clamp(totalScore, -2.5, 2.5) / 2.5; // Normalized to [-1.0, 1.0]
        
        bool isMlActive = (mlSignal.Direction == "BUY" || mlSignal.Direction == "PUT");
        double finalConfidenceScore = scoreMath;
        string candidateDir = "NEUTRAL";

        if (isMlActive)
        {
            // True Ensemble: Blend Machine Learning (60% weight) with Pure Math (40% weight)
            double normLgbm = Math.Max(0, (mlSignal.Confidence - 0.5) * 2.0);
            double mlScore = mlSignal.Direction == "BUY" ? normLgbm : -normLgbm;
            
            double mlWeight = options?.Value.MlWeight ?? 0.5;
            double mathWeight = options?.Value.MathWeight ?? 0.5;
            finalConfidenceScore = (mlScore * mlWeight) + (scoreMath * mathWeight);
        }
        
        candidateDir = finalConfidenceScore > 0.0001 ? "BUY" : finalConfidenceScore < -0.0001 ? "PUT" : "NEUTRAL";

        // 5. Final Decision
        double absWeightedScore = Math.Abs(finalConfidenceScore);
        int probability = isSubMinute
            ? Math.Clamp(50 + (int)Math.Round(absWeightedScore * 40), 50, 91)
            : Math.Clamp(50 + (int)Math.Round(absWeightedScore * 45), 50, 95);

        // MTF Golden Boost вЂ” only apply when 4D dominant direction MATCHES candidateDir.
        // FIX: Previously boosted unconditionally, inflating probability even when MTF
        //      was pointing in the OPPOSITE direction to the final candidate signal.
        if (candidateDir != "NEUTRAL"
            && mtfResult.ProbabilityBoost > 0
            && (mtfResult.DominantDirection == candidateDir
                || mtfResult.DominantDirection == "NEUTRAL"))
        {
            probability = Math.Clamp(probability + mtfResult.ProbabilityBoost, 55, 95);
        }

        // 6. Reasoning text
        string modelAccText = mlSignal.Accuracy.HasValue
            ? $" [РўРѕС‡РЅРѕСЃС‚СЊ: {Math.Round(mlSignal.Accuracy.Value * 100, 1)}%]"
            : "";

        string smcText = !string.IsNullOrEmpty(smcSignal.Reasoning)
            ? $"\u2022 \U0001f6e1\ufe0f SMC РЎС‚СЂСѓРєС‚СѓСЂР°: {smcSignal.Reasoning}"
            : "\u2022 \U0001f6e1\ufe0f SMC РЎС‚СЂСѓРєС‚СѓСЂР°: Р‘Р°Р»Р°РЅСЃРѕРІР°СЏ РєРѕРЅСЃРѕР»РёРґР°С†РёСЏ РґРёР°РїР°Р·РѕРЅР°";

        string flowText = !string.IsNullOrEmpty(ofSignal.Description)
            ? $"\u2022 \U0001f30a Order Flow & CVD: {ofSignal.Description}"
            : "\u2022 \U0001f30a Order Flow & CVD: РџРѕС‚РѕРє РѕСЂРґРµСЂРѕРІ СЃР±Р°Р»Р°РЅСЃРёСЂРѕРІР°РЅ";

        string lgbmText = !string.IsNullOrEmpty(mlSignal.Direction) && mlSignal.Direction != "NEUTRAL"
            ? $"\u2022 \u26a1 РќРµР№СЂРѕСЃРµС‚СЊ (LightGBM): {(mlSignal.Direction == "BUY" ? "Р’Р’Р•Р РҐ \u2b06" : "Р’РќРР— \u2b07")} ({Math.Round(mlSignal.Confidence * 100)}% СѓРІРµСЂРµРЅРЅРѕСЃС‚СЊ){modelAccText}"
            : (mlSignal.ModelVersion == "disabled" 
                ? $"\u2022 \u26a1 РќРµР№СЂРѕСЃРµС‚СЊ (LightGBM): РћС‚РєР»СЋС‡РµРЅР° РїРѕР»СЊР·РѕРІР°С‚РµР»РµРј"
                : $"\u2022 \u26a1 РќРµР№СЂРѕСЃРµС‚СЊ (LightGBM): РќР•Р™РўР РђР›Р¬РќРћ (0% СѓРІРµСЂРµРЅРЅРѕСЃС‚СЊ){modelAccText}");

        string combinedReasoning = $"{smcText}\n{flowText}\n{lgbmText}";

        return new ConsensusDecision(candidateDir, candidateDir, probability, combinedReasoning, totalScore);
    }

}
