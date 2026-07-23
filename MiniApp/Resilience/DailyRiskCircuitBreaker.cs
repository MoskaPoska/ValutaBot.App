using System;
using System.Collections.Concurrent;

namespace ValutaBot.MiniApp;

public record DailyRiskStatus(
    bool IsTradeAllowed,
    double CurrentDailyLossPercent,
    int ConsecutiveLosses,
    string StatusReasoning
);

/// <summary>
/// Daily Max Drawdown Guard & Capital Protection Engine.
/// Automatically blocks 1-Click auto-trade execution if cumulative daily losses
/// exceed 15% or 4 consecutive losses occur, protecting user account balance.
/// </summary>
public static class DailyRiskCircuitBreaker
{
    private class UserDailyState
    {
        public string DateKey { get; set; } = DateTime.UtcNow.ToString("yyyy-MM-dd");
        public double StartingBalanceUsd { get; set; } = 100.0;
        public double CumulativeLossUsd { get; set; } = 0.0;
        public int ConsecutiveLosses { get; set; } = 0;
        public bool HardBlockedForDay { get; set; } = false;
    }

    private static readonly ConcurrentDictionary<long, UserDailyState> _userRiskStore = new();

    /// <summary>
    /// Evaluates whether auto-trade execution is allowed for a user.
    /// </summary>
    public static DailyRiskStatus EvaluateRiskStatus(long chatId, double currentBalanceUsd = 100.0)
    {
        string today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var state = _userRiskStore.GetOrAdd(chatId, _ => new UserDailyState { DateKey = today, StartingBalanceUsd = currentBalanceUsd });

        lock (state)
        {
            // Reset state if calendar day changed
            if (state.DateKey != today)
            {
                state.DateKey = today;
                state.StartingBalanceUsd = currentBalanceUsd;
                state.CumulativeLossUsd = 0;
                state.ConsecutiveLosses = 0;
                state.HardBlockedForDay = false;
            }

            if (state.HardBlockedForDay)
            {
                return new DailyRiskStatus(
                    IsTradeAllowed: false,
                    CurrentDailyLossPercent: Math.Round((state.CumulativeLossUsd / state.StartingBalanceUsd) * 100, 1),
                    ConsecutiveLosses: state.ConsecutiveLosses,
                    StatusReasoning: "🛑 Авто-трейдинг заблокирован до конца дня: Превышен дневной лимит просадки (Daily Max Drawdown Circuit Breaker)."
                );
            }

            double lossPercent = (state.CumulativeLossUsd / Math.Max(1.0, state.StartingBalanceUsd)) * 100.0;
            if (lossPercent >= 15.0)
            {
                state.HardBlockedForDay = true;
                BotLogger.Warn($"[Daily Risk Guard] User {chatId} reached 15% daily drawdown limit ({lossPercent:F1}%). Locking auto-trade for the day.");
                return new DailyRiskStatus(false, Math.Round(lossPercent, 1), state.ConsecutiveLosses, "🛑 Достигнут лимит просадки 15% за день.");
            }

            if (state.ConsecutiveLosses >= 4)
            {
                state.HardBlockedForDay = true;
                BotLogger.Warn($"[Daily Risk Guard] User {chatId} hit 4 consecutive losses. Locking auto-trade for the day.");
                return new DailyRiskStatus(false, Math.Round(lossPercent, 1), state.ConsecutiveLosses, "🛑 Сработала защита: 4 убытка подряд за день.");
            }

            return new DailyRiskStatus(true, Math.Round(lossPercent, 1), state.ConsecutiveLosses, "✅ Риск-параметры в норме.");
        }
    }

    /// <summary>
    /// Records trade outcome to update daily risk metrics.
    /// </summary>
    public static void RecordTradeResult(long chatId, double tradeAmountUsd, bool wasWin)
    {
        string today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var state = _userRiskStore.GetOrAdd(chatId, _ => new UserDailyState { DateKey = today });

        lock (state)
        {
            if (state.DateKey != today)
            {
                state.DateKey = today;
                state.CumulativeLossUsd = 0;
                state.ConsecutiveLosses = 0;
                state.HardBlockedForDay = false;
            }

            if (wasWin)
            {
                state.ConsecutiveLosses = 0;
                state.CumulativeLossUsd = Math.Max(0, state.CumulativeLossUsd - tradeAmountUsd * 0.85);
            }
            else
            {
                state.ConsecutiveLosses++;
                state.CumulativeLossUsd += tradeAmountUsd;

                double lossPercent = (state.CumulativeLossUsd / Math.Max(1.0, state.StartingBalanceUsd)) * 100.0;
                if (lossPercent >= 15.0 || state.ConsecutiveLosses >= 4)
                {
                    state.HardBlockedForDay = true;
                    BotLogger.Warn($"[Daily Risk Guard] Locked user {chatId} for day after loss. Drawdown: {lossPercent:F1}%.");
                }
            }
        }
    }
}
