using System;
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
    IMarketAnalyzer marketAnalyzer) : IConfluenceMatrixEngine
{
    // ── 4D Matrix ─────────────────────────────────────────────────────────────

    public async Task<ConfluenceMatrixResult> Evaluate4DMatrixAsync(
        string asset,
        string primaryTimeframe,
        bool isForex = false,
        string? binanceSymbol = null)
    {
        var (microTf, primaryTf, macroTf, globalTf) = Resolve4DTimeframes(primaryTimeframe);

        try
        {
            var microTask   = fetcher.FetchBinanceWithFallback(binanceSymbol, microTf,   asset, 40);
            var primaryTask = fetcher.FetchBinanceWithFallback(binanceSymbol, primaryTf, asset, 40);
            var macroTask   = fetcher.FetchBinanceWithFallback(binanceSymbol, macroTf,   asset, 40);
            var globalTask  = fetcher.FetchBinanceWithFallback(binanceSymbol, globalTf,  asset, 40);

            await Task.WhenAll(microTask, primaryTask, macroTask, globalTask);

            var (microPrices,   microVolumes)   = await microTask;
            var (primaryPrices, primaryVolumes) = await primaryTask;
            var (macroPrices,   macroVolumes)   = await macroTask;
            var (globalPrices,  globalVolumes)  = await globalTask;

            string dirMicro   = ScoreDirection(microPrices,   microVolumes);
            string dirPrimary = ScoreDirection(primaryPrices, primaryVolumes);
            string dirMacro   = ScoreDirection(macroPrices,   macroVolumes);
            string dirGlobal  = ScoreDirection(globalPrices,  globalVolumes);

            var tfDirs = new Dictionary<string, string>
            {
                [microTf.ToUpper()]   = dirMicro,
                [primaryTf.ToUpper()] = dirPrimary,
                [macroTf.ToUpper()]   = dirMacro,
                [globalTf.ToUpper()]  = dirGlobal
            };

            var counts    = tfDirs.Values.GroupBy(d => d).ToDictionary(g => g.Key, g => g.Count());
            int buyCount  = counts.GetValueOrDefault("BUY", 0);
            int putCount  = counts.GetValueOrDefault("PUT", 0);
            int maxAgree  = Math.Max(buyCount, putCount);

            double confluenceRatio = Math.Round(maxAgree / 4.0, 2);
            string dominantDir     = buyCount == putCount ? "NEUTRAL"
                                   : buyCount > putCount ? "BUY" : "PUT";
            bool isGoldenSetup     = confluenceRatio >= 0.99;

            int boost = confluenceRatio switch
            {
                >= 0.99 => 12,
                >= 0.75 => 6,
                _       => 0
            };

            string label = confluenceRatio switch
            {
                >= 0.99 => "\U0001f31f ЗОЛОТОЙ СЕТАП (4D 100%)",
                >= 0.75 => "\u26a1 СИЛЬНОЕ СОВПАДЕНИЕ (3D 75%)",
                _       => "\U0001f4ca СТАНДАРТНЫЙ АНАЛИЗ (50%)"
            };

            string summary = $"\u2022 \U0001f3af 4D Матрица ({microTf.ToUpper()}+{primaryTf.ToUpper()}+{macroTf.ToUpper()}+{globalTf.ToUpper()}): {label}";

            BotLogger.Info($"[Confluence 4D] {asset} | Ratio: {confluenceRatio * 100}% ({maxAgree}/4 {dominantDir}) | Boost: +{boost}% | Golden: {isGoldenSetup}");

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
            BotLogger.Error($"[Confluence 4D] Error evaluating matrix for {asset}", ex);
            throw new Exception($"ОТКАЗ API: Ошибка расчета 4D Матрицы. Недостаточно данных для {asset}. {ex.Message}");
        }
    }

    private static (string micro, string primary, string macro, string global)
        Resolve4DTimeframes(string tf) =>
        tf.ToLower() switch
        {
            "s3" or "s5" or "s10" or "s15" or "s30" => ("m1",  "m3",  "m5",  "m15"),
            "m1"                                     => ("s30", "m1",  "m5",  "h1"),
            "m2" or "m3"                             => ("m1",  "m3",  "m15", "h1"),
            "m5"                                     => ("m1",  "m5",  "m15", "h1"),
            "m15"                                    => ("m5",  "m15", "h1",  "h4"),
            _                                        => ("s30", "m1",  "m5",  "h1")
        };

    /// <summary>
    /// Scores directional bias for a single timeframe using the full
    /// TechnicalAnalysisEngine pipeline (HMA, ConnorsRSI, ADX, Volume).
    ///
    /// FIX: Previously passed candles=null to ScoreTimeframe, which caused
    /// candles.Length == 0 &lt; 14 → always return score=0.0 → always "NEUTRAL".
    /// Now constructs a real OhlcCandle[] from price/volume arrays.
    /// </summary>
    private string ScoreDirection(double[] prices, double[] volumes)
    {
        if (prices == null || prices.Length < 10) 
        {
            throw new Exception($"ОТКАЗ API: Получено {(prices == null ? 0 : prices.Length)} свечей для матрицы (нужно мин 10).");
        }

        double avgDiff = 0;
        if (prices.Length > 1) {
            for (int k = 1; k < prices.Length; k++) avgDiff += Math.Abs(prices[k] - prices[k - 1]);
            avgDiff /= (prices.Length - 1);
        }
        if (avgDiff == 0) avgDiff = prices[0] * 0.0001;

        var candles = new MiniAppController.OhlcCandle[prices.Length];
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
            candles: candles
        );

        return score > 0.10 ? "BUY" : score < -0.10 ? "PUT" : "NEUTRAL";
    }

    // ── Unified Matrix Evaluation ──────────────────────────────────────────────

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

        // 1. Technical Analysis (Base)
        double taWeight  = await SignalTracker.GetSignalWeightAsync("INDICATORS", 1.0);
        totalScore      += (taSignal.Score + ofSignal.ScoreContribution) * taWeight;
        totalConfidence += taSignal.Confidence * taWeight;
        totalWeight     += taWeight;

        // 2. Velocity / Continuous State
        double stateWeight  = await SignalTracker.GetSignalWeightAsync("VelocityState", 1.5);
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
            double smcWeight  = await SignalTracker.GetSignalWeightAsync("SMC", 1.0);
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
            
            finalConfidenceScore = (mlScore * 0.6) + (scoreMath * 0.4);
        }
        
        candidateDir = finalConfidenceScore > 0.0001 ? "BUY" : finalConfidenceScore < -0.0001 ? "PUT" : "NEUTRAL";
        
        // Elimination of NEUTRAL for Pocket Option (Always output a vector)
        if (candidateDir == "NEUTRAL")
        {
            candidateDir = finalConfidenceScore >= 0 ? "BUY" : "PUT";
        }

        // 5. Final Decision
        double absWeightedScore = Math.Abs(finalConfidenceScore);
        int probability = isSubMinute
            ? Math.Clamp(50 + (int)Math.Round(absWeightedScore * 40), 50, 91)
            : Math.Clamp(50 + (int)Math.Round(absWeightedScore * 45), 50, 95);

        // MTF Golden Boost — only apply when 4D dominant direction MATCHES candidateDir.
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
            ? $" [Точность: {Math.Round(mlSignal.Accuracy.Value * 100, 1)}%]"
            : "";

        string smcText = !string.IsNullOrEmpty(smcSignal.Reasoning)
            ? $"\u2022 \U0001f6e1\ufe0f SMC Структура: {smcSignal.Reasoning}"
            : "\u2022 \U0001f6e1\ufe0f SMC Структура: Балансовая консолидация диапазона";

        string flowText = !string.IsNullOrEmpty(ofSignal.Description)
            ? $"\u2022 \U0001f30a Order Flow & CVD: {ofSignal.Description}"
            : "\u2022 \U0001f30a Order Flow & CVD: Поток ордеров сбалансирован";

        string lgbmText = !string.IsNullOrEmpty(mlSignal.Direction) && mlSignal.Direction != "NEUTRAL"
            ? $"\u2022 \u26a1 Нейросеть (LightGBM): {(mlSignal.Direction == "BUY" ? "ВВЕРХ \u2b06" : "ВНИЗ \u2b07")} ({Math.Round(mlSignal.Confidence * 100)}% уверенность){modelAccText}"
            : $"\u2022 \u26a1 Нейросеть (LightGBM): НЕЙТРАЛЬНО (0% уверенность){modelAccText}";

        string combinedReasoning = $"{smcText}\n{flowText}\n{lgbmText}\n{mtfResult.SummaryReasoning}";

        return new ConsensusDecision(candidateDir, candidateDir, probability, combinedReasoning, totalScore);
    }
}
