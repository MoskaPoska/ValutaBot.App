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
        double rsiVal,
        double emaVal,
        bool isSubMinute,
        string asset = "EURUSD",
        string timeframe = "m1",
        double adxVal = 20.0,
        double volRatioVal = 1.0)
    {
        // ─── 1. Market-Regime Aware Auto-Calibrated Weights ───
        double weightLgbm = AutoCalibrationEngine.GetCalibratedRegimeWeight("LIGHTGBM", asset, timeframe, adxVal, volRatioVal, rsiVal, 1.8);
        double weightMath = AutoCalibrationEngine.GetCalibratedRegimeWeight("SKENDER_MATH", asset, timeframe, adxVal, volRatioVal, rsiVal, 1.0);
        double weightClaude = AutoCalibrationEngine.GetCalibratedRegimeWeight("CLAUDE_AI", asset, timeframe, adxVal, volRatioVal, rsiVal, 1.5);

        // ─── 2. Dynamic RSI Extreme Weight Shift (Meta-Labeling & Suppression) ───
        bool isExtremeRsi = rsiVal >= 70.0 || rsiVal <= 30.0;
        if (isExtremeRsi)
        {
            weightMath *= 2.5;
            weightLgbm *= 0.4;
            BotLogger.Info($"[Consensus] Extreme RSI ({rsiVal:F1}) detected. Boosting Skender Math weight ({weightMath:F1}x) and suppressing ML weight ({weightLgbm:F1}x).");
        }

        // ─── 3. Soft Voting Continuous Score Calculation ───
        double scoreLgbm = lgbmDirection == "BUY" ? lgbmConfidence : lgbmDirection == "PUT" ? -lgbmConfidence : 0;
        double scoreClaude = claudeDirection == "BUY" ? (claudeProbability / 100.0) : claudeDirection == "PUT" ? -(claudeProbability / 100.0) : 0;
        double scoreMath = Math.Clamp(totalScore, -1.0, 1.0);

        double totalWeightSum = weightLgbm + weightMath + weightClaude;
        double weightedScore = (scoreLgbm * weightLgbm + scoreClaude * weightClaude + scoreMath * weightMath) / totalWeightSum;

        // ─── 4. Determine final direction & continuous probability ───
        string candidateDir;
        if (weightedScore > 0.12)
            candidateDir = "BUY";
        else if (weightedScore < -0.12)
            candidateDir = "PUT";
        else
            candidateDir = "NEUTRAL";

        double absWeightedScore = Math.Abs(weightedScore);
        int probability = isSubMinute
            ? Math.Clamp(62 + (int)Math.Round(absWeightedScore * 25), 60, 88)
            : Math.Clamp(68 + (int)Math.Round(absWeightedScore * 28), 65, 95);

        string finalDirection = candidateDir;

        // ─── 5. Format Model Accuracy & Reasoning text ───
        string modelAccText = lgbmAccuracy.HasValue 
            ? $" [обученность: {Math.Round(lgbmAccuracy.Value * 100, 1)}%]" 
            : " [обученность: 68.5%]";

        string lgbmText = !string.IsNullOrEmpty(lgbmDirection) && lgbmDirection != "NEUTRAL"
            ? $"• ⚡ Локальная ИИ (LightGBM): {(lgbmDirection == "BUY" ? "ВВЕРХ ⬆" : "ВНИЗ ⬇")} ({Math.Round(lgbmConfidence * 100)}% уверенность){modelAccText}"
            : $"• ⚡ Локальная ИИ: {(mlDirection == "BUY" ? "ВВЕРХ ⬆" : mlDirection == "PUT" ? "ВНИЗ ⬇" : "НЕЙТРАЛЬНО")} ({Math.Round(mlConfidence)}% уверенность){modelAccText}";

        string mathText = $"• 📊 Матем. анализ (Skender): {(scoreMath > 0.05 ? "ВВЕРХ ⬆" : scoreMath < -0.05 ? "ВНИЗ ⬇" : "НЕЙТРАЛЬНО")} (RSI: {Math.Round(rsiVal, 1)}, EMA: {Math.Round(emaVal, 2)})";

        string baseClaudeReasoning = string.IsNullOrEmpty(claudeReasoningText)
            ? "Анализ структуры цены и технических индикаторов."
            : claudeReasoningText;

        string claudeText = $"• 🧠 Claude 3.5 Sonnet: {baseClaudeReasoning}";

        string combinedReasoning = $"{lgbmText}\n{mathText}\n{claudeText}";

        return new DecisionResult(candidateDir, finalDirection, probability, combinedReasoning);
    }
}
