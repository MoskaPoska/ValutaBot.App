namespace ValutaBot.MiniApp;

/// <summary>
/// Order Flow & Volume Delta Imbalance Engine for Forex & OTC market pairs.
/// Analyzes Cumulative Volume Delta (CVD), Buyer/Seller aggressiveness ratio,
/// and Limit Order Absorption (Поглощение ордеров) to detect real-time market pressure.
/// </summary>
public static class OrderFlowEngine
{
    public record OrderFlowResult(
        double BuyVolume,
        double SellVolume,
        double DeltaRatio,
        string OrderFlowState, // "STRONG_BULLISH_FLOW" | "STRONG_BEARISH_FLOW" | "BULLISH_ABSORPTION" | "BEARISH_ABSORPTION" | "BALANCED"
        double ScoreContribution,
        string Description
    );

    public static OrderFlowResult AnalyzeOrderFlow(double[] prices, double[] volumes, MiniAppController.OhlcCandle[]? candles = null)
    {
        if (prices == null || prices.Length < 5 || volumes == null || volumes.Length < 5)
        {
            return new OrderFlowResult(0, 0, 1.0, "BALANCED", 0, "Недостаточно свечей для анализа потока ордеров.");
        }

        int n = Math.Min(prices.Length, volumes.Length);
        double totalBuyVol = 0;
        double totalSellVol = 0;

        if (candles != null && candles.Length >= 5)
        {
            int checkCount = Math.Min(10, candles.Length);
            for (int i = candles.Length - checkCount; i < candles.Length; i++)
            {
                var c = candles[i];
                double totalVol = c.Volume > 0 ? c.Volume : 1.0;
                double range = c.High - c.Low;

                if (range > 1e-9)
                {
                    // Estimate buying/selling volume pressure based on candle close location within high-low range
                    double buyRatio = (c.Close - c.Low) / range;
                    double sellRatio = (c.High - c.Close) / range;

                    totalBuyVol += totalVol * buyRatio;
                    totalSellVol += totalVol * sellRatio;
                }
                else
                {
                    totalBuyVol += totalVol * 0.5;
                    totalSellVol += totalVol * 0.5;
                }
            }
        }
        else
        {
            int checkCount = Math.Min(10, n);
            for (int i = n - checkCount; i < n; i++)
            {
                double priceDiff = i > 0 ? prices[i] - prices[i - 1] : 0;
                double vol = volumes[i] > 0 ? volumes[i] : 1.0;

                if (priceDiff > 0) totalBuyVol += vol;
                else if (priceDiff < 0) totalSellVol += vol;
                else
                {
                    totalBuyVol += vol * 0.5;
                    totalSellVol += vol * 0.5;
                }
            }
        }

        double deltaRatio = totalSellVol > 1e-8 ? totalBuyVol / totalSellVol : 1.0;
        double priceDelta = prices[^1] - prices[Math.Max(0, prices.Length - 5)];

        string state;
        double scoreContribution = 0;
        string desc;

        // Detect Limit Order Absorption (High volume but price stuck -> passive wall absorbing orders)
        if (deltaRatio > 1.7 && priceDelta < -1e-6)
        {
            state = "BEARISH_ABSORPTION";
            scoreContribution = -0.3;
            desc = "Поглощение покупок пассивными лимитными продажами (Bearish Absorption).";
        }
        else if (deltaRatio < 0.58 && priceDelta > 1e-6)
        {
            state = "BULLISH_ABSORPTION";
            scoreContribution = 0.3;
            desc = "Поглощение продаж пассивными лимитными покупками (Bullish Absorption).";
        }
        else if (deltaRatio >= 1.6)
        {
            state = "STRONG_BULLISH_FLOW";
            scoreContribution = 0.4;
            desc = $"Агрессивное преобладание покупателей (Order Flow Ratio: {deltaRatio:F2}x).";
        }
        else if (deltaRatio <= 0.62)
        {
            state = "STRONG_BEARISH_FLOW";
            scoreContribution = -0.4;
            desc = $"Агрессивное преобладание продавцов (Order Flow Ratio: {1.0 / Math.Max(0.01, deltaRatio):F2}x).";
        }
        else
        {
            state = "BALANCED";
            scoreContribution = 0;
            desc = "Поток ордеров сбалансирован.";
        }

        return new OrderFlowResult(
            Math.Round(totalBuyVol, 1),
            Math.Round(totalSellVol, 1),
            Math.Round(deltaRatio, 2),
            state,
            scoreContribution,
            desc
        );
    }
}
