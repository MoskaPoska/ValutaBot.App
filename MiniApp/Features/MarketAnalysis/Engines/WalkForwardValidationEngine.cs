using System;
using System.Collections.Concurrent;

namespace ValutaBot.MiniApp;

/// <summary>
/// Drawdown Protection & Anti-Overfitting Regime Protection Engine.
/// Prevents consecutive losses during sudden market regime shifts & news events
/// by tracking drawdown cooloff phases.
/// </summary>
public class WalkForwardValidationEngine : IWalkForwardValidationEngine
{
    private class CooloffState
    {
        public int ConsecutiveLosses { get; set; }
        public DateTime CooloffUntil { get; set; } = DateTime.MinValue;
    }

    private readonly ConcurrentDictionary<SignalKey, CooloffState> _cooloffMap = new();

    /// <summary>
    /// Validates current performance to prevent drawdown losses during regime shifts.
    /// </summary>
    public WalkForwardResult ValidateWalkForward(
        string asset,
        string timeframe,
        double[] prices,
        bool isNewsActive = false)
    {
        var key = new SignalKey(asset, timeframe);
        var cooloff = _cooloffMap.GetOrAdd(key, _ => new CooloffState());

        // 1. Check if Cooloff Phase is active (triggered after 3 consecutive losses)
        bool isCooloffActive;
        DateTime cooloffUntil;
        lock (cooloff)
        {
            cooloffUntil = cooloff.CooloffUntil;
            isCooloffActive = DateTime.UtcNow < cooloffUntil;
        }

        if (isCooloffActive)
        {
            BotLogger.Warn($"[Drawdown Protection] Cooloff active for {key} until {cooloffUntil:HH:mm:ss}. ML weight suppressed to 0.1x.");
            return new WalkForwardResult(
                IsOverfitted: true,
                IsCooloffActive: true,
                WeightMultiplier: 0.10,
                StatusReasoning: "Фаза охлаждения после серии убытков (Drawdown Protection Active).",
                CooloffUntil: cooloffUntil
            );
        }

        // 2. If High-Impact News is active, suppress ML and rely on SMC / Quant Math
        if (isNewsActive)
        {
            BotLogger.Warn($"[News Blackout] High-Impact News active for {key}. Clamping ML weight.");
            return new WalkForwardResult(
                IsOverfitted: true,
                IsCooloffActive: false,
                WeightMultiplier: 0.20,
                StatusReasoning: "Выход новостей высокой важности (News Blackout Active)."
            );
        }

        if (prices == null || prices.Length < 30)
        {
            return new WalkForwardResult(false, false, 1.0, "Недостаточно свечей.");
        }

        return new WalkForwardResult(
            IsOverfitted: false,
            IsCooloffActive: false,
            WeightMultiplier: 1.0,
            StatusReasoning: "Авто-калибровка весов (AutoCalibrationEngine активен)."
        );
    }

    /// <summary>
    /// Records trade outcome to manage drawdown cooloff phase.
    /// Triggers 15-minute cooloff if 3 consecutive losses occur.
    /// </summary>
    public void RecordTradeOutcome(string asset, string timeframe, bool isWin)
    {
        var key = new SignalKey(asset, timeframe);
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

    public readonly record struct SignalKey(string Asset, string Timeframe)
    {
        public override string ToString() => $"{Asset}_{Timeframe}";
    }

    public readonly record struct WalkForwardResult(
        bool IsOverfitted,
        bool IsCooloffActive,
        double WeightMultiplier,
        string StatusReasoning,
        DateTime CooloffUntil = default
    );
}

