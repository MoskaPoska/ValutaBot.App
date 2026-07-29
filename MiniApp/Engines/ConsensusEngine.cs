namespace ValutaBot.MiniApp;

/// <summary>
/// Decision Consensus Engine: Implements Soft Voting & Dynamic Extreme Weighting.
/// Dynamically suppresses ML hallucinations on RSI extreme boundaries (>70 / <30)
/// and calculates continuous probabilities across LightGBM, Claude AI, and Skender Math.
/// </summary>
public static class ConsensusEngine
{
    public record DecisionResult(
        string CandidateDirection,
        string FinalDirection,
        int Probability,
        string CombinedReasoningText,
        string RecommendedExpiryText = ""
    );

    public static DecisionResult EvaluateConsensus(
        double totalScore,
        double scoreSign,
        string claudeDirection,
        int claudeProbability,
        string claudeReasoningText,
        string lgbmDirection,
        double lgbmConfidence,
        double? lgbmAccuracy,
        string mlDirection,
        double mlConfidence,
        string onnxDirection,
        double onnxConfidence,
        double rsiVal,
        double emaVal,
        bool isSubMinute,
        string asset = "EURUSD",
        string timeframe = "m1",
        double adxVal = 20.0,
        double volRatioVal = 1.0,
        string smcReasoning = "",
        string orderFlowReasoning = "",
        string aiModelName = "РР РђРЅР°Р»РёР·",
        double wfWeightMultiplier = 1.0)
    {
        // в”Ђв”Ђв”Ђ 1. Market-Regime Aware Auto-Calibrated Weights в”Ђв”Ђв”Ђ
        double weightLgbm = AutoCalibrationEngine.GetCalibratedRegimeWeight("LIGHTGBM", asset, timeframe, adxVal, volRatioVal, rsiVal, 1.8);
        double weightMath = AutoCalibrationEngine.GetCalibratedRegimeWeight("SKENDER_MATH", asset, timeframe, adxVal, volRatioVal, rsiVal, 1.2);
        // ONNX is also regime-aware now вЂ” base weight 2.2 is scaled by market regime + win-rate
        double weightOnnx = AutoCalibrationEngine.GetCalibratedRegimeWeight("ONNX", asset, timeframe, adxVal, volRatioVal, rsiVal, 2.2);
        double weightMl   = AutoCalibrationEngine.GetCalibratedRegimeWeight("NATIVE_ML", asset, timeframe, adxVal, volRatioVal, rsiVal, 1.0);

        // в”Ђв”Ђв”Ђ 3. HFT Soft Voting Vector Calculation (0% LLM Weight in Decision Pipeline) в”Ђв”Ђв”Ђ
        // Fix: Normalize [0.5, 1.0] confidence to [0.0, 1.0] vector magnitude. 50% confidence means 0 pull.
        double normLgbm = Math.Max(0, (lgbmConfidence - 0.5) * 2.0);
        double normMl   = Math.Max(0, (mlConfidence - 0.5) * 2.0);
        double normOnnx = Math.Max(0, (onnxConfidence - 0.5) * 2.0);

        double scoreLgbm = lgbmDirection == "BUY" ? normLgbm : lgbmDirection == "PUT" ? -normLgbm : 0;
        double scoreMl   = mlDirection   == "BUY" ? normMl   : mlDirection   == "PUT" ? -normMl   : 0;
        double scoreOnnx = onnxDirection == "BUY" ? normOnnx : onnxDirection == "PUT" ? -normOnnx : 0;
        double scoreMath = Math.Clamp(totalScore, -2.5, 2.5) / 2.5; // Normalize Math score to [-1.0, 1.0]

        // Only include weights for models that actually provided a directional opinion.
        // Apply Walk-Forward Anti-Overfitting Multiplier to ML engines
        double activeWeightLgbm = (lgbmDirection == "BUY" || lgbmDirection == "PUT") ? weightLgbm * wfWeightMultiplier : 0;
        double activeWeightMl   = (mlDirection   == "BUY" || mlDirection   == "PUT") ? weightMl   * wfWeightMultiplier : 0;
        double activeWeightOnnx = (onnxDirection == "BUY" || onnxDirection == "PUT") ? weightOnnx * wfWeightMultiplier : 0;
        // Math is always active since it produces a continuous score
        double activeWeightMath = weightMath;
        
        double totalWeightSum = activeWeightLgbm + activeWeightMl + activeWeightOnnx + activeWeightMath;
        if (totalWeightSum < 1e-9) totalWeightSum = 1.0;
        
        double weightedScore = (scoreLgbm * activeWeightLgbm + scoreMl * activeWeightMl + scoreOnnx * activeWeightOnnx + scoreMath * activeWeightMath) / totalWeightSum;

        string candidateDir = weightedScore > 0.0001 ? "BUY" : weightedScore < -0.0001 ? "PUT" : (totalScore > 0.02 ? "BUY" : totalScore < -0.02 ? "PUT" : "NEUTRAL");

        double absWeightedScore = Math.Abs(weightedScore);
        int probability = isSubMinute
            ? Math.Clamp(50 + (int)Math.Round(absWeightedScore * 40), 50, 91)
            : Math.Clamp(50 + (int)Math.Round(absWeightedScore * 45), 50, 95);

        string finalDirection = candidateDir;

        // в”Ђв”Ђв”Ђ 5. Format 4 Pillars of Analysis Breakdown в”Ђв”Ђв”Ђ
        string modelAccText = lgbmAccuracy.HasValue 
            ? $" [РѕР±СѓС‡РµРЅРЅРѕСЃС‚СЊ: {Math.Round(lgbmAccuracy.Value * 100, 1)}%]" 
            : "";

        string smcText = !string.IsNullOrEmpty(smcReasoning)
            ? $"вЂў рџЏ›пёЏ SMC РЎС‚СЂСѓРєС‚СѓСЂР°: {smcReasoning}"
            : "вЂў рџЏ›пёЏ SMC РЎС‚СЂСѓРєС‚СѓСЂР°: Р‘Р°Р»Р°РЅСЃРѕРІР°СЏ РєРѕРЅСЃРѕР»РёРґР°С†РёСЏ РґРёР°РїР°Р·РѕРЅР°";

        string flowText = !string.IsNullOrEmpty(orderFlowReasoning)
            ? $"вЂў рџЊЉ Order Flow & CVD: {orderFlowReasoning}"
            : "вЂў рџЊЉ Order Flow & CVD: РџРѕС‚РѕРє РѕСЂРґРµСЂРѕРІ СЃР±Р°Р»Р°РЅСЃРёСЂРѕРІР°РЅ";

        string lgbmText = !string.IsNullOrEmpty(lgbmDirection) && lgbmDirection != "NEUTRAL"
            ? $"вЂў вљЎ РќРµР№СЂРѕСЃРµС‚СЊ (LightGBM): {(lgbmDirection == "BUY" ? "Р’Р’Р•Р РҐ в¬†" : "Р’РќРР— в¬‡")} ({Math.Round(lgbmConfidence * 100)}% СѓРІРµСЂРµРЅРЅРѕСЃС‚СЊ){modelAccText}"
            : $"вЂў вљЎ РќРµР№СЂРѕСЃРµС‚СЊ (LightGBM): {(mlDirection == "BUY" ? "Р’Р’Р•Р РҐ в¬†" : mlDirection == "PUT" ? "Р’РќРР— в¬‡" : "РќР•Р™РўР РђР›Р¬РќРћ")} ({Math.Round(mlConfidence)}% СѓРІРµСЂРµРЅРЅРѕСЃС‚СЊ){modelAccText}";

        string effectiveModelName = string.IsNullOrEmpty(aiModelName) ? "РњР°С‚РµРјР°С‚РёС‡РµСЃРєРёР№ РР" : aiModelName;

        string baseClaudeReasoning = string.IsNullOrEmpty(claudeReasoningText)
            ? $"РњР°С‚РµРј. Р°РЅР°Р»РёР· Skender (RSI: {Math.Round(rsiVal, 1)}, EMA: {Math.Round(emaVal, 2)})"
            : claudeReasoningText;

        string claudeText = $"вЂў рџ§  {effectiveModelName}: {baseClaudeReasoning}";

        string combinedReasoning = $"{smcText}\n{flowText}\n{lgbmText}\n{claudeText}";

        return new DecisionResult(candidateDir, finalDirection, probability, combinedReasoning);
    }
}
