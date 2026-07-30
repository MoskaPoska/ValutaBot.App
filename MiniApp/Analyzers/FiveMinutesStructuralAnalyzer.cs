using System.Globalization;

namespace ValutaBot.MiniApp;

/// <summary>
/// Structural SMC Core for 5m+ timeframes (5m, 15m, 30m, 1h).
/// Focuses purely on Unmitigated Order Blocks, FVG, BOS structural breaks, and HTF (H1) alignment.
/// Completely disables sub-second tick volume noise.
/// </summary>
public class FiveMinutesStructuralAnalyzer : ITimeframeAnalyzer
{
    public async Task<TimeframeAnalysisResult> AnalyzeAsync(
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
        if (ohlcCandles == null || ohlcCandles.Length < 10 || prices == null || prices.Length == 0)
        {
            return new TimeframeAnalysisResult(
                "WAIT", 0.50, "STRUCTURAL_SMC", "Недостаточно данных для анализа SMC-структуры 5m+.", false
            );
        }

        // ─── 1. Main 5m SMC Structure Analysis ───
        var mainSmc = SmcEngine.AnalyzeSmcStructure(ohlcCandles, prices[^1]);

        // ─── 2. HTF Alignment Validation ───
        SmcEngine.SmcAnalysisResult? htfSmc = null;
        if (higherTfData != null && higherTfData.Value.prices.Length >= 10)
        {
            string htfTf = MarketDataFetcher.Instance.HigherTf(timeframe) ?? "h1";
            string sym = asset.ToUpper().EndsWith("USDT") ? asset : asset + "USDT";
            var higherOhlc = await MarketDataFetcher.Instance.FetchBinanceOhlcCandlesAsync(sym, htfTf);
            if (higherOhlc != null && higherOhlc.Length >= 10)
            {
                htfSmc = SmcEngine.AnalyzeSmcStructure(higherOhlc, higherTfData.Value.prices[^1]);
            }
        }

        var mtfValidation = SmcEngine.ValidateMtfSmcAlignment(mainSmc, htfSmc);

        // ─── 3. Compute SMC Structural Direction & Confidence ───
        string direction = "WAIT";
        double confidence = 0.50;
        string reasoning;

        if (mainSmc.OrderBlockType == "BULLISH_OB" && mainSmc.IsUnmitigatedOb && mtfValidation.IsAlignedWithHtf)
        {
            direction = "BUY";
            confidence = Math.Min(0.95, 0.78 * mtfValidation.ConfluenceMultiplier);
            reasoning = $"[Structural 5m] Свежий бычий Order Block ({mainSmc.OrderBlockLevel:F5}) по тренду H1.";
        }
        else if (mainSmc.OrderBlockType == "BEARISH_OB" && mainSmc.IsUnmitigatedOb && mtfValidation.IsAlignedWithHtf)
        {
            direction = "PUT";
            confidence = Math.Min(0.95, 0.78 * mtfValidation.ConfluenceMultiplier);
            reasoning = $"[Structural 5m] Свежий медвежий Order Block ({mainSmc.OrderBlockLevel:F5}) по тренду H1.";
        }
        else if (mainSmc.BosDirection == "BULLISH_BOS" && mtfValidation.IsAlignedWithHtf)
        {
            direction = "BUY";
            confidence = Math.Min(0.92, 0.75 * mtfValidation.ConfluenceMultiplier);
            reasoning = "[Structural 5m] Подтверждённый пробой структуры ВВЕРХ (BOS) по тренду H1.";
        }
        else if (mainSmc.BosDirection == "BEARISH_BOS" && mtfValidation.IsAlignedWithHtf)
        {
            direction = "PUT";
            confidence = Math.Min(0.92, 0.75 * mtfValidation.ConfluenceMultiplier);
            reasoning = "[Structural 5m] Подтверждённый пробой структуры ВНИЗ (BOS) по тренду H1.";
        }
        else if (!mtfValidation.IsAlignedWithHtf)
        {
            direction = "NEUTRAL";
            confidence = 0.50;
            reasoning = $"[Structural 5m] {mtfValidation.Description}";
        }
        else
        {
            direction = "NEUTRAL";
            confidence = 0.55;
            reasoning = "[Structural 5m] Структура 5m консолидируется без чёткого Order Block.";
        }

        bool isActionable = confidence >= 0.75;
        if (!isActionable && direction != "WAIT" && direction != "NEUTRAL")
        {
            reasoning += " (Уверенность структуры ниже 75%).";
        }

        return new TimeframeAnalysisResult(
            direction,
            Math.Round(confidence, 2),
            "STRUCTURAL_SMC",
            reasoning,
            isActionable
        );
    }
}

