namespace ValutaBot.MiniApp;

/// <summary>
/// Smart Money Concepts (SMC) & Institutional Liquidity Engine.
/// Detects Liquidity Sweeps, Fair Value Gaps (FVG), Unmitigated Order Blocks (OB), Structural Breaks (BOS / CHOCH),
/// and enforces Multi-Timeframe (MTF) HTF Structure Validation (m15/m30 alignment).
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

    public record MtfSmcValidationResult(
        bool IsAlignedWithHtf,
        double ConfluenceMultiplier,
        string AlignmentStatus, // "ALIGNED" | "COUNTER_TREND_CONFLICT" | "NEUTRAL"
        string Description
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

            if (c3.Low > c1.High)
            {
                bullishFvg = true;
                fvgTop = c3.Low;
                fvgBottom = c1.High;
                fvgGapSize = fvgTop - fvgBottom;
            }
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

            if (range > 1e-8 && (body / range) >= 0.60)
            {
                bool isBullishObCandidate = candle.Close < candle.Open && currentPrice > candle.High;
                bool isBearishObCandidate = candle.Close > candle.Open && currentPrice < candle.Low;

                if (isBullishObCandidate || isBearishObCandidate)
                {
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

    /// <summary>
    /// Validates m1 SMC signals against the m15 / m30 Higher Timeframe (HTF) structure.
    /// Heavily penalizes or blocks counter-trend SMC trades against HTF structure.
    /// </summary>
    public static MtfSmcValidationResult ValidateMtfSmcAlignment(SmcAnalysisResult mainSmc, SmcAnalysisResult htfSmc)
    {
        if (htfSmc == null)
        {
            return new MtfSmcValidationResult(true, 1.0, "NEUTRAL", "Старший таймфрейм недоступен, проверка сопоставлена нейтрально.");
        }

        // Determine HTF dominant direction
        bool htfBullish = htfSmc.BosDirection == "BULLISH_BOS" || htfSmc.OrderBlockType == "BULLISH_OB" || htfSmc.FvgType == "BULLISH_FVG";
        bool htfBearish = htfSmc.BosDirection == "BEARISH_BOS" || htfSmc.OrderBlockType == "BEARISH_OB" || htfSmc.FvgType == "BEARISH_FVG";

        // Determine main (m1) signal direction
        bool mainBullish = mainSmc.BosDirection == "BULLISH_BOS" || mainSmc.OrderBlockType == "BULLISH_OB" || mainSmc.FvgType == "BULLISH_FVG";
        bool mainBearish = mainSmc.BosDirection == "BEARISH_BOS" || mainSmc.OrderBlockType == "BEARISH_OB" || mainSmc.FvgType == "BEARISH_FVG";

        // Counter-trend Conflict: Main signal opposes HTF structure
        if (mainBullish && htfBearish)
        {
            BotLogger.Warn("[MTF SMC Filter] Counter-Trend Conflict! Local m1 BUY signal opposes HTF (m15) BEARISH structure. Signal penalized.");
            return new MtfSmcValidationResult(
                false, 0.30, "COUNTER_TREND_CONFLICT",
                "⚠️ Конфликт со старшим таймфреймом: бычий сетап на m1 против глобального медвежьего тренда m15."
            );
        }
        if (mainBearish && htfBullish)
        {
            BotLogger.Warn("[MTF SMC Filter] Counter-Trend Conflict! Local m1 PUT signal opposes HTF (m15) BULLISH structure. Signal penalized.");
            return new MtfSmcValidationResult(
                false, 0.30, "COUNTER_TREND_CONFLICT",
                "⚠️ Конфликт со старшим таймфреймом: медвежий сетап на m1 против глобального бычьего тренда m15."
            );
        }

        // High Confluence Alignment: Main signal matches HTF structure
        if ((mainBullish && htfBullish) || (mainBearish && htfBearish))
        {
            BotLogger.Info("[MTF SMC Filter] High Confluence Alignment! Local m1 SMC signal perfectly matches HTF m15 structure.");
            return new MtfSmcValidationResult(
                true, 1.40, "ALIGNED",
                "✅ Высокое совпадение: сетап на m1 строго по тренду старшей структуры m15."
            );
        }

        return new MtfSmcValidationResult(true, 1.0, "NEUTRAL", "Нейтральное совпадение со старшей структурой.");
    }
}
