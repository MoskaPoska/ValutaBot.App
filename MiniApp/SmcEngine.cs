namespace ValutaBot.MiniApp;

/// <summary>
/// Smart Money Concepts (SMC) & Institutional Liquidity Engine.
/// Detects Liquidity Sweeps, Fair Value Gaps (FVG), Unmitigated Order Blocks (OB), and Structural Breaks (BOS / CHOCH).
/// </summary>
public static class SmcEngine
{
    public record SmcAnalysisResult(
        bool HasLiquiditySweep,
        string SweepDirection, // "BULLISH_SWEEP" (swept lows) | "BEARISH_SWEEP" (swept highs) | "NONE"
        bool HasFvg,
        string FvgType, // "BULLISH_FVG" | "BEARISH_FVG" | "NONE"
        double FvgTop,
        double FvgBottom,
        double FvgGapSize,
        bool HasOrderBlock,
        string OrderBlockType, // "BULLISH_OB" | "BEARISH_OB" | "NONE"
        double OrderBlockLevel,
        bool IsUnmitigatedOb, // True if the OB has NOT been tested/mitigated by subsequent candles
        bool HasBos,
        string BosDirection, // "BULLISH_BOS" | "BEARISH_BOS" | "NONE"
        string SummaryReasoning
    );

    public static SmcAnalysisResult AnalyzeSmcStructure(MiniAppController.OhlcCandle[] candles, double currentPrice)
    {
        if (candles == null || candles.Length < 10)
        {
            return new SmcAnalysisResult(
                false, "NONE", false, "NONE", 0, 0, 0, false, "NONE", 0, false, false, "NONE", "Недостаточно свечей для SMC-анализа."
            );
        }

        int n = candles.Length;
        var currentCandle = candles[^1];
        var prevCandle = candles[^2];

        // ─── 1. Liquidity Sweep Detection (Снятие ликвидности над/под свинг-уровнями) ───
        double recentHigh = candles.Take(n - 2).TakeLast(15).Max(c => c.High);
        double recentLow = candles.Take(n - 2).TakeLast(15).Min(c => c.Low);

        bool bullishSweep = prevCandle.Low < recentLow && currentCandle.Close > recentLow;
        bool bearishSweep = prevCandle.High > recentHigh && currentCandle.Close < recentHigh;

        string sweepDir = bullishSweep ? "BULLISH_SWEEP" : bearishSweep ? "BEARISH_SWEEP" : "NONE";

        // ─── 2. Fair Value Gap (FVG / Разрыв имбаланса ликвидности) ───
        bool bullishFvg = false;
        bool bearishFvg = false;
        double fvgTop = 0, fvgBottom = 0, fvgGapSize = 0;

        if (n >= 4)
        {
            var c1 = candles[^4];
            var c2 = candles[^3];
            var c3 = candles[^2];

            // Bullish FVG: Low of c3 is strictly higher than High of c1
            if (c3.Low > c1.High)
            {
                bullishFvg = true;
                fvgTop = c3.Low;
                fvgBottom = c1.High;
                fvgGapSize = fvgTop - fvgBottom;
            }
            // Bearish FVG: High of c3 is strictly lower than Low of c1
            else if (c3.High < c1.Low)
            {
                bearishFvg = true;
                fvgTop = c1.Low;
                fvgBottom = c3.High;
                fvgGapSize = fvgTop - fvgBottom;
            }
        }

        string fvgType = bullishFvg ? "BULLISH_FVG" : bearishFvg ? "BEARISH_FVG" : "NONE";

        // ─── 3. Unmitigated Order Block (Свежий, несмягченный блок ордеров) ───
        bool bullishOb = false;
        bool bearishOb = false;
        double obLevel = 0;
        bool isUnmitigatedOb = false;

        for (int i = n - 3; i >= Math.Max(0, n - 15); i--)
        {
            var candle = candles[i];
            double body = Math.Abs(candle.Close - candle.Open);
            double range = candle.High - candle.Low;

            // Displacement criteria: Body must be > 60% of candle range
            if (range > 1e-8 && (body / range) >= 0.60)
            {
                bool isBullishObCandidate = candle.Close < candle.Open && currentPrice > candle.High;
                bool isBearishObCandidate = candle.Close > candle.Open && currentPrice < candle.Low;

                if (isBullishObCandidate || isBearishObCandidate)
                {
                    // Check Mitigation Status: Has any candle after 'i' touched this OB range?
                    bool isMitigated = false;
                    for (int j = i + 1; j < n - 1; j++)
                    {
                        var futureCandle = candles[j];
                        if (isBullishObCandidate && futureCandle.Low <= candle.High)
                        {
                            isMitigated = true;
                            break;
                        }
                        else if (isBearishObCandidate && futureCandle.High >= candle.Low)
                        {
                            isMitigated = true;
                            break;
                        }
                    }

                    // Only accept UNMITIGATED Order Blocks (fresh institutional liquidity)
                    if (!isMitigated)
                    {
                        if (isBullishObCandidate)
                        {
                            bullishOb = true;
                            obLevel = candle.High;
                            isUnmitigatedOb = true;
                            break;
                        }
                        else if (isBearishObCandidate)
                        {
                            bearishOb = true;
                            obLevel = candle.Low;
                            isUnmitigatedOb = true;
                            break;
                        }
                    }
                }
            }
        }

        string obType = bullishOb ? "BULLISH_OB" : bearishOb ? "BEARISH_OB" : "NONE";

        // ─── 4. Break of Structure (BOS / Излом структуры) ───
        bool bullishBos = currentCandle.Close > recentHigh;
        bool bearishBos = currentCandle.Close < recentLow;
        string bosDir = bullishBos ? "BULLISH_BOS" : bearishBos ? "BEARISH_BOS" : "NONE";

        // ─── 5. Summary Reasoning Construction ───
        var summaryParts = new List<string>();
        if (bullishSweep) summaryParts.Add("Снятие ликвидности покупателей (Bullish Sweep)");
        else if (bearishSweep) summaryParts.Add("Снятие ликвидности продавцов (Bearish Sweep)");

        if (bullishFvg) summaryParts.Add($"Бычий FVG имбаланс [{fvgBottom:F5} - {fvgTop:F5}]");
        else if (bearishFvg) summaryParts.Add($"Медвежий FVG имбаланс [{fvgBottom:F5} - {fvgTop:F5}]");

        if (bullishOb) summaryParts.Add($"Свежий бычий Order Block ({obLevel:F5}) [Unmitigated]");
        else if (bearishOb) summaryParts.Add($"Свежий медвежий Order Block ({obLevel:F5}) [Unmitigated]");

        if (bullishBos) summaryParts.Add("Пробой структуры ВВЕРХ (BOS)");
        else if (bearishBos) summaryParts.Add("Пробой структуры ВНИЗ (BOS)");

        string summaryText = summaryParts.Count > 0
            ? string.Join(" | ", summaryParts)
            : "Структура консолидируется в нейтральном диапазоне.";

        return new SmcAnalysisResult(
            bullishSweep || bearishSweep, sweepDir,
            bullishFvg || bearishFvg, fvgType, fvgTop, fvgBottom, fvgGapSize,
            bullishOb || bearishOb, obType, obLevel, isUnmitigatedOb,
            bullishBos || bearishBos, bosDir,
            summaryText
        );
    }
}
