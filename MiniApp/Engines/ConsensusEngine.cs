using System;

namespace ValutaBot.MiniApp;

/// <summary>
/// Decision Consensus Engine: Implements Soft Voting & Dynamic Extreme Weighting.
/// Calculates continuous probabilities using LightGBM and Skender Math.
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
        string lgbmDirection,
        double lgbmConfidence,
        double? lgbmAccuracy,
        double rsiVal,
        double emaVal,
        bool isSubMinute,
        string asset = "EURUSD",
        string timeframe = "m1",
        double adxVal = 20.0,
        double volRatioVal = 1.0,
        string smcReasoning = "",
        string orderFlowReasoning = "",
        double wfWeightMultiplier = 1.0)
    {
        double weightLgbm = AutoCalibrationEngine.GetCalibratedRegimeWeight("LIGHTGBM", asset, timeframe, adxVal, volRatioVal, rsiVal, 1.8);
        double weightMath = AutoCalibrationEngine.GetCalibratedRegimeWeight("SKENDER_MATH", asset, timeframe, adxVal, volRatioVal, rsiVal, 1.2);

        double normLgbm = Math.Max(0, (lgbmConfidence - 0.5) * 2.0);

        double scoreLgbm = lgbmDirection == "BUY" ? normLgbm : lgbmDirection == "PUT" ? -normLgbm : 0;
        double scoreMath = Math.Clamp(totalScore, -2.5, 2.5) / 2.5; 

        double activeWeightLgbm = (lgbmDirection == "BUY" || lgbmDirection == "PUT") ? weightLgbm * wfWeightMultiplier : 0;
        double activeWeightMath = weightMath;
        
        double totalWeightSum = activeWeightLgbm + activeWeightMath;
        if (totalWeightSum < 1e-9) totalWeightSum = 1.0;
        
        double weightedScore = (scoreLgbm * activeWeightLgbm + scoreMath * activeWeightMath) / totalWeightSum;

        string candidateDir = weightedScore > 0.0001 ? "BUY" : weightedScore < -0.0001 ? "PUT" : (totalScore > 0.02 ? "BUY" : totalScore < -0.02 ? "PUT" : "NEUTRAL");

        double absWeightedScore = Math.Abs(weightedScore);
        int probability = isSubMinute
            ? Math.Clamp(50 + (int)Math.Round(absWeightedScore * 40), 50, 91)
            : Math.Clamp(50 + (int)Math.Round(absWeightedScore * 45), 50, 95);

        string finalDirection = candidateDir;

        string modelAccText = lgbmAccuracy.HasValue 
            ? $" [Точность: {Math.Round(lgbmAccuracy.Value * 100, 1)}%]" 
            : "";

        string smcText = !string.IsNullOrEmpty(smcReasoning)
            ? $"• 🏛️ SMC Структура: {smcReasoning}"
            : "• 🏛️ SMC Структура: Балансовая консолидация диапазона";

        string flowText = !string.IsNullOrEmpty(orderFlowReasoning)
            ? $"• 🌊 Order Flow & CVD: {orderFlowReasoning}"
            : "• 🌊 Order Flow & CVD: Поток ордеров сбалансирован";

        string lgbmText = !string.IsNullOrEmpty(lgbmDirection) && lgbmDirection != "NEUTRAL"
            ? $"• ⚡ Нейросеть (LightGBM): {(lgbmDirection == "BUY" ? "ВВЕРХ ⬆" : "ВНИЗ ⬇")} ({Math.Round(lgbmConfidence * 100)}% уверенность){modelAccText}"
            : $"• ⚡ Нейросеть (LightGBM): НЕЙТРАЛЬНО (0% уверенность){modelAccText}";

        string combinedReasoning = $"{smcText}\n{flowText}\n{lgbmText}";

        return new DecisionResult(candidateDir, finalDirection, probability, combinedReasoning);
    }
}
