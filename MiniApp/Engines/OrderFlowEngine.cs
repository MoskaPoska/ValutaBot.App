namespace ValutaBot.MiniApp;

/// <summary>
/// Order Flow & Volume Delta Imbalance Engine for Forex & OTC market pairs.
/// Filters out HFT micro-noise, TWAP/VWAP algorithmic noise, and Spoofing traps.
/// Focuses exclusively on Institutional Block Trades, Volume Cluster Anomalies, and Real Momentum Progress.
/// </summary>
public static class OrderFlowEngine

{
    public record OrderFlowResult(
        double BuyVolume,
        double SellVolume,
        double DeltaRatio,
        double CumulativeVolumeDelta,
        string OrderFlowState, // "STRONG_BULLISH_FLOW" | "STRONG_BEARISH_FLOW" | "BULLISH_ABSORPTION" | "BEARISH_ABSORPTION" | "SPOOFING_TRAP" | "BALANCED"
        double ScoreContribution,
        bool IsInstitutionalBlockTrade,
        string Description
    );

    public static OrderFlowResult AnalyzeOrderFlow(
        ReadOnlySpan<double> prices,
        ReadOnlySpan<double> volumes,
        ReadOnlySpan<MiniAppController.OhlcCandle> candles = default)
    {
        if (prices == null || prices.Length < 5 || volumes == null || volumes.Length < 5)
        {
            return new OrderFlowResult(0, 0, 1.0, 0, "BALANCED", 0, false, "Недостаточно свечей для анализа потока ордеров.");
        }

        int n = Math.Min(prices.Length, volumes.Length);
        
        // ─── 1. Calculate Volume Baseline & Threshold for Block Trades ───
        int spanStart = Math.Max(0, n - 21);
        int spanLen = Math.Min(20, n - 1 - spanStart);
        var volWindow = volumes.Slice(spanStart, spanLen);
        double sum = 0;
        int count = 0;
        foreach (var v in volWindow)
        {
            if (v > 0)
            {
                sum += v;
                count++;
            }
        }
        double avgVolume = count > 0 ? sum / count : 1.0;
        double noiseThreshold = avgVolume * 0.60;      // Filter out micro-trade noise
        double blockTradeThreshold = avgVolume * 1.70;  // Threshold for Institutional Anomaly Clusters

        double totalBuyVol = 0;
        double totalSellVol = 0;
        bool hasInstitutionalBlockTrade = false;
        double cumulativeVolumeDelta = 0;

        if (!candles.IsEmpty && candles.Length >= 5)
        {
            int checkCount = Math.Min(12, candles.Length);
            for (int i = candles.Length - checkCount; i < candles.Length; i++)
            {
                var c = candles[i];
                double totalVol = c.Volume > 0 ? c.Volume : 1.0;

                // ─── Filter 1: Ignore Noise Micro-Trades ───
                if (totalVol < noiseThreshold)
                    continue;

                // ─── Filter 2: Flag Institutional Block Trades ───
                if (totalVol >= blockTradeThreshold)
                    hasInstitutionalBlockTrade = true;

                double range = c.High - c.Low;
                if (range > 1e-9)
                {
                    double buyRatio = (c.Close - c.Low) / range;
                    double sellRatio = (c.High - c.Close) / range;

                    double buyV = totalVol * buyRatio;
                    double sellV = totalVol * sellRatio;
                    totalBuyVol += buyV;
                    totalSellVol += sellV;
                    cumulativeVolumeDelta += (buyV - sellV);
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
            int checkCount = Math.Min(12, n);
            for (int i = n - checkCount; i < n; i++)
            {
                double vol = volumes[i] > 0 ? volumes[i] : 1.0;

                if (vol < noiseThreshold)
                    continue;

                if (vol >= blockTradeThreshold)
                    hasInstitutionalBlockTrade = true;

                double priceDiff = i > 0 ? prices[i] - prices[i - 1] : 0;
                if (priceDiff > 0)
                {
                    totalBuyVol += vol;
                    cumulativeVolumeDelta += vol;
                }
                else if (priceDiff < 0)
                {
                    totalSellVol += vol;
                    cumulativeVolumeDelta -= vol;
                }
                else
                {
                    totalBuyVol += vol * 0.5;
                    totalSellVol += vol * 0.5;
                }
            }
        }

        double deltaRatio = totalSellVol > 1e-8 ? totalBuyVol / totalSellVol : Math.Max(1.0, totalBuyVol);
        double priceDelta = prices[^1] - prices[Math.Max(0, prices.Length - 5)];

        string state;
        double scoreContribution = 0;
        string desc;

        // ─── 2. Spoofing Detection (must come FIRST — overlaps with Absorption on deltaRatio > 1.8) ───
        // Spoofing: high buy volume but price goes NOWHERE → artificial order book manipulation
        if (deltaRatio > 1.8 && cumulativeVolumeDelta > 0 && priceDelta < -1e-9)
        {
            state = "BEARISH_ABSORPTION";
            scoreContribution = -0.30;
            desc = "CVD Divergence (Bearish Absorption): Скрытые продажи при росте дельты.";
        }
        else if (deltaRatio < 0.55 && cumulativeVolumeDelta < 0 && priceDelta > 1e-9)
        {
            state = "BULLISH_ABSORPTION";
            scoreContribution = 0.30;
            desc = "CVD Divergence (Bullish Absorption): Скрытые покупки при падении дельты.";
        }
        else if (deltaRatio > 1.8 && Math.Abs(priceDelta) < 1e-7)
        {
            state = "SPOOFING_TRAP";
            scoreContribution = 0;
            desc = "Зафиксирована ловушка спуфинга (Spoofing Trap): объем без движения цены.";
        }
        // ─── 3. Passive Limit Absorption Detection ───
        // Bearish Absorption: high buy volume but price FALLS → huge sell wall absorbing all buys
        else if (deltaRatio > 1.8 && priceDelta < -1e-6)
        {
            state = "BEARISH_ABSORPTION";
            scoreContribution = -0.35;
            desc = "Поглощение покупок пассивным лимитным барьером продавца (Bearish Absorption).";
        }
        // Bullish Absorption: high sell volume but price RISES → huge buy wall absorbing all sells
        else if (deltaRatio < 0.55 && priceDelta > 1e-6)
        {
            state = "BULLISH_ABSORPTION";
            scoreContribution = 0.35;
            desc = "Поглощение продаж пассивным лимитным барьером покупателя (Bullish Absorption).";
        }
        // ─── 4. Real Institutional Momentum Flow ───
        else if (deltaRatio >= 1.6 && priceDelta > 0)
        {
            state = "STRONG_BULLISH_FLOW";
            scoreContribution = hasInstitutionalBlockTrade ? 0.5 : 0.35;
            desc = hasInstitutionalBlockTrade
                ? $"Аномальный институциональный блок покупок (Order Flow Ratio: {deltaRatio:F2}x)."
                : $"Преобладание покупателей (Order Flow Ratio: {deltaRatio:F2}x).";
        }
        else if (deltaRatio <= 0.62 && priceDelta < 0)
        {
            state = "STRONG_BEARISH_FLOW";
            scoreContribution = hasInstitutionalBlockTrade ? -0.5 : -0.35;
            double invRatio = 1.0 / Math.Max(0.01, deltaRatio);
            desc = hasInstitutionalBlockTrade
                ? $"Аномальный институциональный блок продаж (Order Flow Ratio: {invRatio:F2}x)."
                : $"Преобладание продавцов (Order Flow Ratio: {invRatio:F2}x).";
        }
        else
        {
            state = "BALANCED";
            scoreContribution = 0;
            desc = "Поток ордеров очищен от шума и находится в балансе.";
        }
        return new OrderFlowResult(
            BuyVolume: Math.Round(totalBuyVol, 1),
            SellVolume: Math.Round(totalSellVol, 1),
            DeltaRatio: Math.Round(deltaRatio, 2),
            CumulativeVolumeDelta: Math.Round(cumulativeVolumeDelta, 2),
            OrderFlowState: state,
            ScoreContribution: Math.Round(scoreContribution, 2),
            IsInstitutionalBlockTrade: hasInstitutionalBlockTrade,
            Description: desc
        );
    }
}
