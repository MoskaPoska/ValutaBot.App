using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ValutaBot.MiniApp;

public static class TradeOutcomeTracker
{
    private static bool _initialized = false;
    private static readonly object _initLock = new();

    /// <summary>
    /// Initializes trade outcome tracking engine and restores online learning state from SQLite DB.
    /// </summary>
    public static void Initialize()
    {
        if (_initialized) return;
        lock (_initLock)
        {
            if (_initialized) return;

            try
            {
                var outcomes = BotDatabase.LoadTradeOutcomes(1000);
                BotLogger.Info($"[TradeOutcomeTracker] Loaded {outcomes.Count} historical outcomes from SQLite DB.");

                foreach (var outcome in outcomes)
                {
                    AutoCalibrationEngine.RecordSourceOutcome("GLOBAL", outcome.Asset, outcome.Timeframe, outcome.WasWin);
                }

                _initialized = true;
                BotLogger.Info("[TradeOutcomeTracker] Online Reinforcement Learning engine initialized successfully.");
            }
            catch (Exception ex)
            {
                BotLogger.Error("[TradeOutcomeTracker] Failed to initialize trade outcome tracker", ex);
            }
        }
    }

    /// <summary>
    /// Triggered automatically by SignalTracker when a trade candle expires and exit price is verified.
    /// Updates SQLite DB, AutoCalibrationEngine weights, and triggers Python LightGBM Online RL update.
    /// </summary>
    public static void OnTradeVerified(SignalTracker.PredictionRecord record)
    {
        Initialize();

        try
        {
            var outcomeRecord = new TradeOutcomeRecord
            {
                Id = record.Id,
                Asset = record.Asset,
                Timeframe = record.Timeframe,
                Direction = record.Direction,
                EntryPrice = record.EntryPrice,
                ExitPrice = record.ExitPrice ?? record.EntryPrice,
                PnlBps = record.PnlBps,
                WasWin = record.WasCorrect ?? false,
                CreatedAt = record.CreatedAt.ToString("o"),
                VerifiedAt = DateTime.UtcNow.ToString("o")
            };

            // 1. Persist to ACID SQLite Database
            BotDatabase.SaveTradeOutcome(outcomeRecord);

            bool wasCorrect = record.WasCorrect ?? false;
            double exitPriceVal = record.ExitPrice ?? record.EntryPrice;

            // 2. Continuous Online RL for AutoCalibration Engine
            if (record.SourceDirections != null && record.SourceDirections.Count > 0)
            {
                string winDirection = exitPriceVal > record.EntryPrice ? "BUY" : "PUT";
                foreach (var kv in record.SourceDirections)
                {
                    if (kv.Value != "NEUTRAL")
                    {
                        bool wasSourceCorrect = (kv.Value == winDirection);
                        AutoCalibrationEngine.RecordSourceOutcome(kv.Key, record.Asset, record.Timeframe, wasSourceCorrect);
                    }
                }
            }
            else
            {
                AutoCalibrationEngine.RecordSourceOutcome("GLOBAL", record.Asset, record.Timeframe, wasCorrect);
                AutoCalibrationEngine.RecordSourceOutcome("LIGHTGBM", record.Asset, record.Timeframe, wasCorrect);
                AutoCalibrationEngine.RecordSourceOutcome("SKENDER_MATH", record.Asset, record.Timeframe, wasCorrect);
                AutoCalibrationEngine.RecordSourceOutcome("SMC", record.Asset, record.Timeframe, wasCorrect);
            }

            // 3. Continuous Online RL for Python LightGBM ML Service
            _ = Task.Run(async () =>
            {
                try
                {
                    await MLPythonService.RecordOnlineTradeOutcomeAsync(
                        record.Asset,
                        record.Timeframe,
                        record.EntryPrice,
                        exitPriceVal,
                        record.Direction,
                        wasCorrect
                    );
                }
                catch (Exception mlEx)
                {
                    Console.WriteLine($"[TradeOutcomeTracker] Online ML update notice: {mlEx.Message}");
                }
            });

            BotLogger.Info($"[TradeOutcomeTracker] Verified trade {record.Id} ({record.Asset} {record.Timeframe}) -> {(wasCorrect ? "WIN ✅" : "LOSS ❌")}. Online RL weights updated.");
        }
        catch (Exception ex)
        {
            BotLogger.Error($"[TradeOutcomeTracker] Error processing trade outcome for {record.Id}", ex);
        }
    }
}
