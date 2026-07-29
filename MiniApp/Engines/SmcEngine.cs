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
                false, "NONE", false, "NONE", 0, 0, 0, false, "NONE", 0, false, false, "NONE", "РќРµРґРѕСЃС‚Р°С‚РѕС‡РЅРѕ СЃРІРµС‡РµР№ РґР»СЏ SMC-Р°РЅР°Р»РёР·Р°."
            );
        }

        int n = candles.Length;
        // Use only closed candles to prevent repainting
        var closedCandle = candles[^2];
        var prevClosedCandle = candles[^3];

        // в”Ђв”Ђв”Ђ 1. Liquidity Sweep Detection (РЎРЅСЏС‚РёРµ Р»РёРєРІРёРґРЅРѕСЃС‚Рё РЅР°Рґ/РїРѕРґ СЃРІРёРЅРі-СѓСЂРѕРІРЅСЏРјРё) в”Ђв”Ђв”Ђ
        double recentHigh = candles.Take(n - 3).TakeLast(15).Max(c => c.High);
        double recentLow = candles.Take(n - 3).TakeLast(15).Min(c => c.Low);

        bool bullishSweep = prevClosedCandle.Low < recentLow && closedCandle.Close > recentLow;
        bool bearishSweep = prevClosedCandle.High > recentHigh && closedCandle.Close < recentHigh;

        string sweepDir = bullishSweep ? "BULLISH_SWEEP" : bearishSweep ? "BEARISH_SWEEP" : "NONE";

        // в”Ђв”Ђв”Ђ 2. Fair Value Gap (FVG / Р Р°Р·СЂС‹РІ РёРјР±Р°Р»Р°РЅСЃР° Р»РёРєРІРёРґРЅРѕСЃС‚Рё) в”Ђв”Ђв”Ђ
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

        // в”Ђв”Ђв”Ђ 3. Unmitigated Order Block (РЎРІРµР¶РёР№, РЅРµСЃРјСЏРіС‡РµРЅРЅС‹Р№ Р±Р»РѕРє РѕСЂРґРµСЂРѕРІ) в”Ђв”Ђв”Ђ
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
                    double obBodyTop = Math.Max(candle.Open, candle.Close);
                    double obBodyBottom = Math.Min(candle.Open, candle.Close);

                    for (int j = i + 1; j < n - 1; j++)
                    {
                        var futureCandle = candles[j];
                        if (isBullishObCandidate && futureCandle.Low <= obBodyBottom)
                        {
                            isMitigated = true;
                            break;
                        }
                        else if (isBearishObCandidate && futureCandle.High >= obBodyTop)
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

        // в”Ђв”Ђв”Ђ 4. Break of Structure (BOS / РР·Р»РѕРј СЃС‚СЂСѓРєС‚СѓСЂС‹) в”Ђв”Ђв”Ђ
        bool bullishBos = closedCandle.Close > recentHigh;
        bool bearishBos = closedCandle.Close < recentLow;
        string bosDir = bullishBos ? "BULLISH_BOS" : bearishBos ? "BEARISH_BOS" : "NONE";

        // в”Ђв”Ђв”Ђ 5. Summary Reasoning Construction в”Ђв”Ђв”Ђ
        var summaryParts = new List<string>();
        if (bullishSweep) summaryParts.Add("РЎРЅСЏС‚РёРµ Р»РёРєРІРёРґРЅРѕСЃС‚Рё РїРѕРєСѓРїР°С‚РµР»РµР№ (Bullish Sweep)");
        else if (bearishSweep) summaryParts.Add("РЎРЅСЏС‚РёРµ Р»РёРєРІРёРґРЅРѕСЃС‚Рё РїСЂРѕРґР°РІС†РѕРІ (Bearish Sweep)");

        if (bullishFvg) summaryParts.Add($"Р‘С‹С‡РёР№ FVG РёРјР±Р°Р»Р°РЅСЃ [{fvgBottom:F5} - {fvgTop:F5}]");
        else if (bearishFvg) summaryParts.Add($"РњРµРґРІРµР¶РёР№ FVG РёРјР±Р°Р»Р°РЅСЃ [{fvgBottom:F5} - {fvgTop:F5}]");

        if (bullishOb) summaryParts.Add($"РЎРІРµР¶РёР№ Р±С‹С‡РёР№ Order Block ({obLevel:F5}) [Unmitigated]");
        else if (bearishOb) summaryParts.Add($"РЎРІРµР¶РёР№ РјРµРґРІРµР¶РёР№ Order Block ({obLevel:F5}) [Unmitigated]");

        if (bullishBos) summaryParts.Add("РџСЂРѕР±РѕР№ СЃС‚СЂСѓРєС‚СѓСЂС‹ Р’Р’Р•Р РҐ (BOS)");
        else if (bearishBos) summaryParts.Add("РџСЂРѕР±РѕР№ СЃС‚СЂСѓРєС‚СѓСЂС‹ Р’РќРР— (BOS)");

        string summaryText = summaryParts.Count > 0
            ? string.Join(" | ", summaryParts)
            : "РЎС‚СЂСѓРєС‚СѓСЂР° РєРѕРЅСЃРѕР»РёРґРёСЂСѓРµС‚СЃСЏ РІ РЅРµР№С‚СЂР°Р»СЊРЅРѕРј РґРёР°РїР°Р·РѕРЅРµ.";

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
            return new MtfSmcValidationResult(true, 1.0, "NEUTRAL", "РЎС‚Р°СЂС€РёР№ С‚Р°Р№РјС„СЂРµР№Рј РЅРµРґРѕСЃС‚СѓРїРµРЅ, РїСЂРѕРІРµСЂРєР° СЃРѕРїРѕСЃС‚Р°РІР»РµРЅР° РЅРµР№С‚СЂР°Р»СЊРЅРѕ.");
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
                "вљ пёЏ РљРѕРЅС„Р»РёРєС‚ СЃРѕ СЃС‚Р°СЂС€РёРј С‚Р°Р№РјС„СЂРµР№РјРѕРј: Р±С‹С‡РёР№ СЃРµС‚Р°Рї Р»РѕРєР°Р»СЊРЅРѕР№ СЃС‚СЂСѓРєС‚СѓСЂС‹ РїСЂРѕС‚РёРІ РіР»РѕР±Р°Р»СЊРЅРѕРіРѕ РјРµРґРІРµР¶СЊРµРіРѕ С‚СЂРµРЅРґР°."
            );
        }
        if (mainBearish && htfBullish)
        {
            BotLogger.Warn("[MTF SMC Filter] Counter-Trend Conflict! Local PUT signal opposes HTF BULLISH structure. Signal penalized.");
            return new MtfSmcValidationResult(
                false, 0.30, "COUNTER_TREND_CONFLICT",
                "вљ пёЏ РљРѕРЅС„Р»РёРєС‚ СЃРѕ СЃС‚Р°СЂС€РёРј С‚Р°Р№РјС„СЂРµР№РјРѕРј: РјРµРґРІРµР¶РёР№ СЃРµС‚Р°Рї Р»РѕРєР°Р»СЊРЅРѕР№ СЃС‚СЂСѓРєС‚СѓСЂС‹ РїСЂРѕС‚РёРІ РіР»РѕР±Р°Р»СЊРЅРѕРіРѕ Р±С‹С‡СЊРµРіРѕ С‚СЂРµРЅРґР°."
            );
        }

        // High Confluence Alignment: Main signal matches dominant HTF structure
        if ((mainBullish && htfBullish) || (mainBearish && htfBearish))
        {
            BotLogger.Info("[MTF SMC Filter] High Confluence Alignment! Local SMC signal perfectly matches HTF structure.");
            return new MtfSmcValidationResult(
                true, 1.40, "ALIGNED",
                "вњ… Р’С‹СЃРѕРєРѕРµ СЃРѕРІРїР°РґРµРЅРёРµ: Р»РѕРєР°Р»СЊРЅС‹Р№ СЃРµС‚Р°Рї СЃС‚СЂРѕРіРѕ РїРѕ С‚СЂРµРЅРґСѓ СЃС‚Р°СЂС€РµР№ СЃС‚СЂСѓРєС‚СѓСЂС‹."
            );
        }

        return new MtfSmcValidationResult(true, 1.0, "NEUTRAL", "РќРµР№С‚СЂР°Р»СЊРЅРѕРµ СЃРѕРІРїР°РґРµРЅРёРµ СЃРѕ СЃС‚Р°СЂС€РµР№ СЃС‚СЂСѓРєС‚СѓСЂРѕР№.");
    }
}
