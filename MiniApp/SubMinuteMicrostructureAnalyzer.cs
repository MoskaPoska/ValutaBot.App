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

        if (orderFlow.OrderFlowState == "STRONG_BULLISH_FLOW" && deltaRatio >= 1.7)
        {
            direction = "BUY";
            confidence = Math.Min(0.92, 0.70 + (deltaRatio - 1.7) * 0.15);
            reasoning = $"[HFT 5s-30s] Агрессивный перекос тиковых покупок (Volume Delta: {deltaRatio:F2}x).";
        }
        else if (orderFlow.OrderFlowState == "STRONG_BEARISH_FLOW" && deltaRatio <= 0.58)
        {
            direction = "PUT";
            double invRatio = 1.0 / Math.Max(0.01, deltaRatio);
            confidence = Math.Min(0.92, 0.70 + (invRatio - 1.7) * 0.15);
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
            direction = "WAIT";
            confidence = 0.55;
            reasoning = "[HFT 5s-30s] Тиковый поток сбалансирован, чёткий микро-импульс отсутствует.";
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
