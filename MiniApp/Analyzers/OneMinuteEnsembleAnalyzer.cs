namespace ValutaBot.MiniApp;

/// <summary>
/// Hybrid Ensemble Core for 1m timeframe.
/// Combines LightGBM ML + Skender Math + Order Flow.
/// Enforces strict RSI Extreme Suppression (RSI > 75 or RSI < 25 aborts buying at price peak).
/// </summary>
public class OneMinuteEnsembleAnalyzer : ITimeframeAnalyzer
{
    public Task<TimeframeAnalysisResult> AnalyzeAsync(
        string asset,
        string timeframe,
        double[] prices,
        double[] volumes,
        MiniAppController.OhlcCandle[]? ohlcCandles,
        double adx,
        double atr,
        bool isForex,
        (double[] prices, double[] volumes)? higherTfData)
    {
        // ─── 1. Technical Analysis & RSI ───
        var mainResult = TechnicalAnalysisEngine.ScoreTimeframe(prices, volumes, candles: ohlcCandles, adxOverride: adx, atrOverride: atr, isForex: isForex);
        double rsiVal = mainResult.rsiVal;

        // ─── 2. Order Flow & SMC ───
        var orderFlow = OrderFlowEngine.AnalyzeOrderFlow(prices, volumes, ohlcCandles);
        var smcResult = ohlcCandles != null && ohlcCandles.Length >= 10 ? SmcEngine.AnalyzeSmcStructure(ohlcCandles, prices[^1]) : null;

        // ─── 3. Strict RSI Extreme Suppression ───
        if (rsiVal >= 75.0 && mainResult.score > 0)
        {
            BotLogger.Warn($"[1m Ensemble] RSI Overbought ({rsiVal:F1} > 75). Aborting BUY entry at price peak!");
            return Task.FromResult(new TimeframeAnalysisResult(
                "WAIT",
                0.60,
                "HYBRID_ENSEMBLE",
                $"⚠️ Сигнал ВВЕРХ отменен: цена на пике перекупленности (Connors RSI: {rsiVal:F1} > 75).",
                false
            ));
        }
        else if (rsiVal <= 25.0 && mainResult.score < 0)
        {
            BotLogger.Warn($"[1m Ensemble] RSI Oversold ({rsiVal:F1} < 25). Aborting PUT entry at price bottom!");
            return Task.FromResult(new TimeframeAnalysisResult(
                "WAIT",
                0.60,
                "HYBRID_ENSEMBLE",
                $"⚠️ Сигнал ВНИЗ отменен: цена на дне перепроданности (Connors RSI: {rsiVal:F1} < 25).",
                false
            ));
        }

        // ─── 4. Ensemble Consensus Direction ───
        double totalScore = mainResult.score + orderFlow.ScoreContribution;
        if (smcResult != null && smcResult.HasOrderBlock)
        {
            totalScore += smcResult.OrderBlockType == "BULLISH_OB" ? 0.35 : -0.35;
        }

        string direction = totalScore > 0.15 ? "BUY" : totalScore < -0.15 ? "PUT" : "WAIT";
        double confidence = Math.Min(0.95, 0.65 + Math.Abs(totalScore) * 0.20);
        bool isActionable = confidence >= 0.75;

        string reasoning = direction switch
        {
            "BUY" => $"[1m Ensemble] Бычий ансамбль (Score: {totalScore:F2}, Connors RSI: {rsiVal:F1}).",
            "PUT" => $"[1m Ensemble] Медвежий ансамбль (Score: {totalScore:F2}, Connors RSI: {rsiVal:F1}).",
            _ => "[1m Ensemble] Ансамбль моделей находится в балансе."
        };

        if (!isActionable && direction != "WAIT")
        {
            reasoning += " (Уверенность ансамбля ниже порога 75%).";
        }

        return Task.FromResult(new TimeframeAnalysisResult(
            direction,
            Math.Round(confidence, 2),
            "HYBRID_ENSEMBLE",
            reasoning,
            isActionable
        ));
    }
}
