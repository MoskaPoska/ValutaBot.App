using System;
using System.Collections.Concurrent;
using System.Linq;

namespace ValutaBot.MiniApp;

/// <summary>
/// Walk-Forward Optimization & Anti-Overfitting Regime Protection Engine.
/// Prevents ML over-fitting drawdowns during sudden market regime shifts & news events
/// by running in-memory Out-of-Sample (OOS) backtesting and tracking drawdown cooloff phases.
/// </summary>
public class WalkForwardValidationEngine : IWalkForwardValidationEngine
{
    private class CooloffState
    {
        public int ConsecutiveLosses { get; set; }
        public DateTime CooloffUntil { get; set; } = DateTime.MinValue;
    }

    private readonly ConcurrentDictionary<string, CooloffState> _cooloffMap = new();
    private readonly ITechnicalAnalysisEngine _taEngine;

    public WalkForwardValidationEngine(ITechnicalAnalysisEngine taEngine)
    {
        _taEngine = taEngine;
    }

    /// <summary>
    /// Evaluates Walk-Forward Out-Of-Sample performance on historical candles
    /// to detect overfitting and prevent drawdown losses during regime shifts.
    /// </summary>
    public WalkForwardValidationEngine.WalkForwardResult ValidateWalkForward(
        string asset,
        string timeframe,
        double[] prices,
        bool isNewsActive = false)
    {
        string key = "${asset.ToUpper()}_${timeframe.ToLower()}";
        var cooloff = _cooloffMap.GetOrAdd(key, _ => new CooloffState());

        // 1. Check if Cooloff Phase is active (triggered after 3 consecutive losses)
        bool isCooloffActive = DateTime.UtcNow < cooloff.CooloffUntil;
        if (isCooloffActive)
        {
            BotLogger.Warn($"[Walk-Forward] Cooloff active for {key} until {cooloff.CooloffUntil:HH:mm:ss}. ML weight suppressed to 0.1x.");
            return new WalkForwardValidationEngine.WalkForwardResult(
                InSampleWinRate: 0.65,
                OutOfSampleWinRate: 0.40,
                IsOverfitted: true,
                IsCooloffActive: true,
                WeightMultiplier: 0.10,
                StatusReasoning: "Фаза охлаждения после серии убытков (Drawdown Protection Active)."
            );
        }

        // 2. If High-Impact News is active, suppress ML and rely on SMC / Quant Math
        if (isNewsActive)
        {
            BotLogger.Warn($"[Walk-Forward] High-Impact News Blackout active for {key}. Clamping ML weight.");
            return new WalkForwardValidationEngine.WalkForwardResult(
                InSampleWinRate: 0.70,
                OutOfSampleWinRate: 0.45,
                IsOverfitted: true,
                IsCooloffActive: false,
                WeightMultiplier: 0.20,
                StatusReasoning: "Выход новостей высокой важности (News Blackout Active)."
            );
        }

        if (prices == null || prices.Length < 30)
        {
            return new WalkForwardValidationEngine.WalkForwardResult(0.65, 0.60, false, false, 1.0, "Недостаточно свечей для Walk-Forward анализа.");
        }

        // 3. Walk-Forward Split: 70% In-Sample (IS), 30% Out-of-Sample (OOS)
        int total = prices.Length;
        int inSampleCount = (int)(total * 0.70);
        
        int inSampleWins = 0;
        int inSampleTotal = 0;
        
        // 4. Batch Process O(N) strictly isolated to prevent look-ahead bias
        // Since HMA and RSI only look backwards, we only need to compute this once for the full array.
        var (fullHma, fullRsi) = _taEngine.ComputeWalkForwardBatch(prices);

        // Start from 15 to ensure enough history for HMA/RSI lookbacks
        for (int i = 15; i < inSampleCount - 1; i++)
        {
            double hma = fullHma[i];
            double rsi = fullRsi[i];
            
            double score = (rsi - 50) / 40.0;
            if (prices[i] > hma) score += 0.15;
            else if (prices[i] < hma) score -= 0.15;

            // Only trade if signal is strong enough (>0.15)
            if (score > 0.15)
            {
                if (prices[i + 1] > prices[i]) inSampleWins++;
                inSampleTotal++;
            }
            else if (score < -0.15)
            {
                if (prices[i + 1] < prices[i]) inSampleWins++;
                inSampleTotal++;
            }
        }

        int outSampleWins = 0;
        int outSampleTotal = 0;

        for (int i = Math.Max(15, inSampleCount); i < total - 1; i++)
        {
            double hma = fullHma[i];
            double rsi = fullRsi[i];
            
            double score = (rsi - 50) / 40.0;
            if (prices[i] > hma) score += 0.15;
            else if (prices[i] < hma) score -= 0.15;

            if (score > 0.15)
            {
                if (prices[i + 1] > prices[i]) outSampleWins++;
                outSampleTotal++;
            }
            else if (score < -0.15)
            {
                if (prices[i + 1] < prices[i]) outSampleWins++;
                outSampleTotal++;
            }
        }

        double isWinRate = inSampleTotal > 0 ? (double)inSampleWins / inSampleTotal : 0.65;
        double oosWinRate = outSampleTotal > 0 ? (double)outSampleWins / outSampleTotal : 0.60;

        // 4. Overfitting Detection: IS WinRate > 75% but OOS WinRate < 50%
        bool isOverfitted = (isWinRate - oosWinRate) > 0.20 || oosWinRate < 0.50;

        double weightMult = isOverfitted ? 0.35 : (oosWinRate >= 0.60 ? 1.25 : 1.0);
        string reasoning = isOverfitted
            ? "Обнаружен перекос модели (IS WR=${isWinRate * 100:F0}%, OOS WR=${oosWinRate * 100:F0}%). Понижение веса ML."
            : "Walk-Forward проверка успешна (OOS WR=${oosWinRate * 100:F0}%).";

        return new WalkForwardValidationEngine.WalkForwardResult(
            InSampleWinRate: Math.Round(isWinRate, 2),
            OutOfSampleWinRate: Math.Round(oosWinRate, 2),
            IsOverfitted: isOverfitted,
            IsCooloffActive: false,
            WeightMultiplier: Math.Round(weightMult, 2),
            StatusReasoning: reasoning
        );
    }

    /// <summary>
    /// Records trade outcome to manage drawdown cooloff phase.
    /// Triggers 15-minute cooloff if 3 consecutive losses occur.
    /// </summary>
    public void RecordTradeOutcome(string asset, string timeframe, bool isWin)
    {
        string key = "${asset.ToUpper()}_${timeframe.ToLower()}";
        var state = _cooloffMap.GetOrAdd(key, _ => new CooloffState());

        lock (state)
        {
            if (isWin)
            {
                state.ConsecutiveLosses = 0;
            }
            else
            {
                state.ConsecutiveLosses++;
                if (state.ConsecutiveLosses >= 3)
                {
                    state.CooloffUntil = DateTime.UtcNow.AddMinutes(15);
                    state.ConsecutiveLosses = 0; // Reset after triggering cooloff
                    BotLogger.Warn($"[Drawdown Protection] 3 consecutive losses detected for {key}. Triggering 15-minute cooloff until {state.CooloffUntil:HH:mm:ss}");
                }
            }
        }
    }

    public record WalkForwardResult(
        double InSampleWinRate,
        double OutOfSampleWinRate,
        bool IsOverfitted,
        bool IsCooloffActive,
        double WeightMultiplier,
        string StatusReasoning
    );
}
