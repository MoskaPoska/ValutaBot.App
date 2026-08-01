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
        // Use only closed candles to prevent repainting
        var closedCandle = candles[^2];
        var prevClosedCandle = candles[^3];

        // ─── 0. Calculate Local ATR for Noise Filtering ───
        double atr = 0;
        int atrPeriod = Math.Min(14, n - 1);
        if (atrPeriod > 0)
        {
            double atrSum = 0;
            for (int i = n - 1 - atrPeriod; i < n - 1; i++)
            {
                var c = candles[i];
                atrSum += (c.High - c.Low);
            }
            atr = atrSum / atrPeriod;
        }
        double minFvgGap = atr * 0.20;

        // ─── 1. Liquidity Sweep Detection (Снятие ликвидности над/под свинг-уровнями) ───
        int spanStart = System.Math.Max(0, n - 33);
        int spanLen = System.Math.Min(30, n - 3 - spanStart);
        double recentHigh = candles.Take(n - 3).TakeLast(15).Max(c => c.High);
        double recentLow = candles.Take(n - 3).TakeLast(15).Min(c => c.Low);

        bool bullishSweep = prevClosedCandle.Low < recentLow && closedCandle.Close > recentLow;
        bool bearishSweep = prevClosedCandle.High > recentHigh && closedCandle.Close < recentHigh;

        string sweepDir = bullishSweep ? "BULLISH_SWEEP" : bearishSweep ? "BEARISH_SWEEP" : "NONE";

        // ─── 2. Fair Value Gap (FVG / Разрыв имбаланса ликвидности) ───
        bool bullishFvg = false;
        bool bearishFvg = false;
        double fvgTop = 0, fvgBottom = 0, fvgGapSize = 0;

        if (n >= 4)
        {
            var c1 = candles[^4];
            var c2 = candles[^3];
            var c3 = candles[^2]; // Closed candle

            if (c3.Low > c1.High)
            {
                double gap = c3.Low - c1.High;
                if (gap >= minFvgGap)
                {
                    bullishFvg = true;
                    fvgTop = c3.Low;
                    fvgBottom = c1.High;
                    fvgGapSize = gap;
                }
            }
            else if (c3.High < c1.Low)
            {
                double gap = c1.Low - c3.High;
                if (gap >= minFvgGap)
                {
                    bearishFvg = true;
                    fvgTop = c1.Low;
                    fvgBottom = c3.High;
                    fvgGapSize = gap;
                }
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
                    // Displacement check (The next candle must move aggressively away)
                    var nextCandle = candles[i + 1];
                    bool hasDisplacement = false;
                    if (isBullishObCandidate && nextCandle.Close > nextCandle.Open && (nextCandle.Close - nextCandle.Open) > (atr * 0.4))
                        hasDisplacement = true;
                    if (isBearishObCandidate && nextCandle.Close < nextCandle.Open && (nextCandle.Open - nextCandle.Close) > (atr * 0.4))
                        hasDisplacement = true;

                    if (!hasDisplacement)
                        continue;

                    bool isMitigated = false;
                    double obTop = candle.High;
                    double obBottom = candle.Low;

                    for (int j = i + 1; j < n - 1; j++)
                    {
                        var futureCandle = candles[j];
                        // Mitigated when future price taps the top of the Bullish OB or bottom of the Bearish OB
                        if (isBullishObCandidate && futureCandle.Low <= obTop)
                        {
                            isMitigated = true;
                            break;
                        }
                        else if (isBearishObCandidate && futureCandle.High >= obBottom)
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
        bool bullishBos = closedCandle.Close > recentHigh;
        bool bearishBos = closedCandle.Close < recentLow;
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
    public static MtfSmcValidationResult ValidateMtfSmcAlignment(SmcAnalysisResult mainSmc, SmcAnalysisResult? htfSmc)
    {
        if (htfSmc == null)
        {
            return new MtfSmcValidationResult(true, 1.0, "NEUTRAL", "Старший таймфрейм недоступен, проверка сопоставлена нейтрально.");
        }

        // Compute HTF net directional score (+ for Bullish, - for Bearish)
        int htfScore = 0;
        if (htfSmc.SweepDirection == "BULLISH_SWEEP") htfScore += 2;
        else if (htfSmc.SweepDirection == "BEARISH_SWEEP") htfScore -= 2;
        if (htfSmc.BosDirection == "BULLISH_BOS") htfScore += 2;
        else if (htfSmc.BosDirection == "BEARISH_BOS") htfScore -= 2;
        if (htfSmc.OrderBlockType == "BULLISH_OB") htfScore += 1;
        else if (htfSmc.OrderBlockType == "BEARISH_OB") htfScore -= 1;
        if (htfSmc.FvgType == "BULLISH_FVG") htfScore += 1;
        else if (htfSmc.FvgType == "BEARISH_FVG") htfScore -= 1;

        // Compute local net directional score
        int mainScore = 0;
        if (mainSmc.SweepDirection == "BULLISH_SWEEP") mainScore += 2;
        else if (mainSmc.SweepDirection == "BEARISH_SWEEP") mainScore -= 2;
        if (mainSmc.BosDirection == "BULLISH_BOS") mainScore += 2;
        else if (mainSmc.BosDirection == "BEARISH_BOS") mainScore -= 2;
        if (mainSmc.OrderBlockType == "BULLISH_OB") mainScore += 1;
        else if (mainSmc.OrderBlockType == "BEARISH_OB") mainScore -= 1;
        if (mainSmc.FvgType == "BULLISH_FVG") mainScore += 1;
        else if (mainSmc.FvgType == "BEARISH_FVG") mainScore -= 1;

        bool htfBullish = htfScore > 0;
        bool htfBearish = htfScore < 0;
        bool mainBullish = mainScore > 0;
        bool mainBearish = mainScore < 0;

        // Counter-trend Conflict: Main signal opposes dominant HTF structure
        if (mainBullish && htfBearish)
        {
            BotLogger.Warn("[MTF SMC Filter] Counter-Trend Conflict! Local BUY signal opposes HTF BEARISH structure. Signal penalized.");
            return new MtfSmcValidationResult(
                false, 0.30, "COUNTER_TREND_CONFLICT",
                "⚠️ Конфликт со старшим таймфреймом: бычий сетап локальной структуры против глобального медвежьего тренда."
            );
        }
        if (mainBearish && htfBullish)
        {
            BotLogger.Warn("[MTF SMC Filter] Counter-Trend Conflict! Local PUT signal opposes HTF BULLISH structure. Signal penalized.");
            return new MtfSmcValidationResult(
                false, 0.30, "COUNTER_TREND_CONFLICT",
                "⚠️ Конфликт со старшим таймфреймом: медвежий сетап локальной структуры против глобального бычьего тренда."
            );
        }

        // High Confluence Alignment: Main signal matches dominant HTF structure
        if ((mainBullish && htfBullish) || (mainBearish && htfBearish))
        {
            BotLogger.Info("[MTF SMC Filter] High Confluence Alignment! Local SMC signal perfectly matches HTF structure.");
            return new MtfSmcValidationResult(
                true, 1.40, "ALIGNED",
                "✅ Высокое совпадение: локальный сетап строго по тренду старшей структуры."
            );
        }

        return new MtfSmcValidationResult(true, 1.0, "NEUTRAL", "Нейтральное совпадение со старшей структурой.");
    }
}
