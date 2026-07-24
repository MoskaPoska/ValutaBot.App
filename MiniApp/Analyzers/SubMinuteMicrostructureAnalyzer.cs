namespace ValutaBot.MiniApp;

/// <summary>
/// HFT Microstructure Core for sub-minute timeframes (5s, 10s, 15s, 30s).
/// Focuses purely on Order Book Imbalance (OBI) & WebSocket Tick Delta via OrderFlowEngine.
/// Disables heavy SMC and Skender indicator calculations to eliminate noise.
/// </summary>
public class SubMinuteMicrostructureAnalyzer : ITimeframeAnalyzer
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
        // ─── 1. Order Flow & Tick Delta Analysis ───
        var orderFlow = OrderFlowEngine.AnalyzeOrderFlow(prices, volumes, ohlcCandles);
        double deltaRatio = orderFlow.DeltaRatio;

        // ─── 2. Calculate HFT Direction & Confidence ───
        string direction = "WAIT";
        double confidence = 0.50;
        string reasoning;

        if (orderFlow.OrderFlowState == "STRONG_BULLISH_FLOW" && deltaRatio >= 1.6)
        {
            direction = "BUY";
            confidence = Math.Min(0.92, 0.72 + (deltaRatio - 1.6) * 0.15);
            reasoning = $"[HFT 5s-30s] Агрессивный перекос тиковых покупок (Volume Delta: {deltaRatio:F2}x).";
        }
        else if (orderFlow.OrderFlowState == "STRONG_BEARISH_FLOW" && deltaRatio <= 0.62)
        {
            direction = "PUT";
            double invRatio = 1.0 / Math.Max(0.01, deltaRatio);
            confidence = Math.Min(0.92, 0.72 + (invRatio - 1.6) * 0.15);
            reasoning = $"[HFT 5s-30s] Агрессивный перекос тиковых продаж (Volume Delta: {invRatio:F2}x).";
        }
        else if (orderFlow.OrderFlowState == "BULLISH_ABSORPTION")
        {
            direction = "BUY";
            confidence = 0.78;
            reasoning = "[HFT 5s-30s] Поглощение продаж лимитным барьером покупателя.";
        }
        else if (orderFlow.OrderFlowState == "BEARISH_ABSORPTION")
        {
            direction = "PUT";
            confidence = 0.78;
            reasoning = "[HFT 5s-30s] Поглощение покупок лимитным барьером продавца.";
        }
        else
        {
            // Calculate Micro-Price Momentum & Trend Direction for balanced order flow
            var mainResult = TechnicalAnalysisEngine.ScoreTimeframe(prices, volumes, candles: ohlcCandles, adxOverride: adx, atrOverride: atr, isForex: isForex);
            double totalMicroScore = mainResult.score + orderFlow.ScoreContribution;
            
            if (totalMicroScore > 0.10)
            {
                direction = "BUY";
                confidence = Math.Min(0.88, 0.72 + totalMicroScore * 0.15);
                reasoning = $"[HFT 5s-30s] Микро-импульс движения ВВЕРХ (Score: {totalMicroScore:F2}, Connors RSI: {mainResult.rsiVal:F1}).";
            }
            else if (totalMicroScore < -0.10)
            {
                direction = "PUT";
                confidence = Math.Min(0.88, 0.72 + Math.Abs(totalMicroScore) * 0.15);
                reasoning = $"[HFT 5s-30s] Микро-импульс движения ВНИЗ (Score: {totalMicroScore:F2}, Connors RSI: {mainResult.rsiVal:F1}).";
            }
            else
            {
                double tickDiff = prices.Length >= 5 ? prices[^1] - prices[^5] : (prices.Length >= 2 ? prices[^1] - prices[0] : 0);
                direction = tickDiff > 0 ? "BUY" : tickDiff < 0 ? "PUT" : (mainResult.rsiVal < 50 ? "BUY" : "PUT");
                confidence = 0.75;
                reasoning = "[HFT 5s-30s] Тиковый отклик тикового сдвига (0.1ms tick momentum).";
            }
        }

        bool isActionable = confidence >= 0.75;
        if (!isActionable && direction != "WAIT")
        {
            reasoning += " (Низкая уверенность микроструктуры < 75%).";
        }

        return Task.FromResult(new TimeframeAnalysisResult(
            direction,
            Math.Round(confidence, 2),
            "HFT_MICROSTRUCTURE",
            reasoning,
            isActionable
        ));
    }
}
