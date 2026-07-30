using System;
using System.Threading;
using System.Threading.Tasks;

namespace ValutaBot.MiniApp;

public static class TradeOutcomeTracker
{
    public static IWalkForwardValidationEngine WfEngine { get; set; }
    private static volatile bool _initialized = false;
    private static readonly SemaphoreSlim _initSemaphore = new(1, 1);

    public static async Task InitializeAsync()
    {
        if (_initialized) return;
        
        await _initSemaphore.WaitAsync();
        try
        {
            if (_initialized) return;

            var outcomes = await BotDatabase.LoadTradeOutcomesAsync(1000);
            BotLogger.Info($"[TradeOutcomeTracker] Loaded {outcomes.Count} historical outcomes from PostgreSQL DB.");

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
        finally
        {
            _initSemaphore.Release();
        }
    }

    public static async Task OnTradeVerifiedAsync(SignalTracker.PredictionRecord record)
    {
        await InitializeAsync();

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

            await BotDatabase.SaveTradeOutcomeAsync(outcomeRecord);

            bool wasCorrect = record.WasCorrect ?? false;
            double exitPriceVal = record.ExitPrice ?? record.EntryPrice;

            double priceDiffAbs = Math.Abs(exitPriceVal - record.EntryPrice);
            double priceDiffPct = priceDiffAbs / (record.EntryPrice + 1e-9);

            if (priceDiffPct < 0.00005)
            {
                BotLogger.Info($"[TradeOutcomeTracker] Trade {record.Id} diff {priceDiffPct*10000:F2} bps is below noise threshold. Skipping ML RL update.");
                return;
            }

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
                AutoCalibrationEngine.RecordSourceOutcome("GLOBAL",       record.Asset, record.Timeframe, wasCorrect);
                AutoCalibrationEngine.RecordSourceOutcome("LIGHTGBM",    record.Asset, record.Timeframe, wasCorrect);
                AutoCalibrationEngine.RecordSourceOutcome("SKENDER_MATH", record.Asset, record.Timeframe, wasCorrect);
                AutoCalibrationEngine.RecordSourceOutcome("SMC",          record.Asset, record.Timeframe, wasCorrect);
                AutoCalibrationEngine.RecordSourceOutcome("ONNX",         record.Asset, record.Timeframe, wasCorrect);
                AutoCalibrationEngine.RecordSourceOutcome("NATIVE_ML",    record.Asset, record.Timeframe, wasCorrect);
            }

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

            WfEngine?.RecordTradeOutcome(record.Asset, record.Timeframe, wasCorrect);

            BotLogger.Info($"[TradeOutcomeTracker] Verified trade {record.Id} ({record.Asset} {record.Timeframe}) -> {(wasCorrect ? "WIN" : "LOSS")}. Online RL weights & Walk-Forward state updated.");
        }
        catch (Exception ex)
        {
            BotLogger.Error($"[TradeOutcomeTracker] Error processing trade outcome for {record.Id}", ex);
        }
    }
}
