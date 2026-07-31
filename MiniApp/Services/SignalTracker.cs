using System.Collections.Concurrent;
using System.Net.Http;
using System.Text.Json;

namespace ValutaBot.MiniApp;

/// <summary>
/// Tracks prediction signals and automatically verifies them after the candle expires.
/// Provides per-asset, per-timeframe, and per-source win rate statistics.
/// Now completely stateless (stores pending trades in PostgreSQL).
/// </summary>
public static class SignalTracker
{
    // Cooldown map to prevent duplicate signals spam (fine to stay in memory)
    private static readonly ConcurrentDictionary<string, DateTime> _cooldowns = new();

    private static readonly Timer _verifyTimer;

    static SignalTracker()
    {
        // Run verification every 30 seconds in the background
        _verifyTimer = new Timer(
            _ => Task.Run(async () =>
            {
                try { await VerifyPendingAsync(); }
                catch (Exception ex) { Console.WriteLine($"[Tracker] Verify error: {ex.Message}"); }
            }),
            null,
            TimeSpan.FromSeconds(30),
            TimeSpan.FromSeconds(30)
        );
    }

    // ── Public Write API ───────────────────────────────────────────────────

    /// <summary>
    /// Record a new prediction. Will be verified automatically after expiryCandles × timeframeSecs seconds.
    /// </summary>
    public static async Task RecordPredictionAsync(
        string direction,
        string asset,
        string timeframe,
        double price,
        int expiryCandles = 3,
        int timeframeSecs = 60,
        bool isForex = false,
        string? binanceSymbol = null,
        Dictionary<string, string>? sourceDirections = null)
    {
        string sym = (binanceSymbol ?? MapToBinanceSymbol(asset)).ToUpper();
        int verifyDelaySecs = expiryCandles * timeframeSecs + 5; // +5s buffer for candle close

        string cooldownKey = $"{asset}_{timeframe}";
        if (_cooldowns.TryGetValue(cooldownKey, out var lastSignalAt) && (DateTime.UtcNow - lastSignalAt).TotalSeconds < 30)
        {
            BotLogger.Warn($"[Tracker] Cooldown active for {cooldownKey}. Skipping duplicate signal recording.");
            return;
        }
        _cooldowns[cooldownKey] = DateTime.UtcNow;

        var record = new PredictionRecord
        {
            Id          = Guid.NewGuid().ToString("N")[..8],
            Direction   = direction,
            Asset       = asset,
            Timeframe   = timeframe,
            BinanceSymbol = sym,
            EntryPrice  = price,
            CreatedAt   = DateTime.UtcNow,
            VerifyAt    = DateTime.UtcNow.AddSeconds(verifyDelaySecs),
            IsForex     = isForex,
            SourceDirections = sourceDirections ?? new Dictionary<string, string>()
        };

        await ValutaBot.App.MiniApp.Data.Repositories.TradeRepository.SavePendingTradeAsync(record);

        Console.WriteLine($"[Tracker] Recorded {direction} {asset}/{timeframe} @ {price:F5} " +
                          $"— verify in {verifyDelaySecs}s");
    }

    // ── Public Read API ────────────────────────────────────────────────────

    public static async Task<AccuracyStats> GetOverallStatsAsync()
    {
        var (total, verified, correct) = await ValutaBot.App.MiniApp.Data.Repositories.TradeRepository.GetOverallStatsAsync();
        return new AccuracyStats("ALL", total, verified, correct);
    }

    public static async Task<AccuracyStats> GetStatsAsync(string asset, string timeframe)
    {
        var (total, verified, correct) = await ValutaBot.App.MiniApp.Data.Repositories.TradeRepository.GetStatsAsync(asset, timeframe);
        return new AccuracyStats($"{asset}_{timeframe}", total, verified, correct);
    }

    public static async Task<AccuracyStats[]> GetAllStatsAsync()
    {
        var rows = await ValutaBot.App.MiniApp.Data.Repositories.TradeRepository.GetAllStatsAsync();
        return rows.Select(r => new AccuracyStats($"{r.asset}_{r.timeframe}", r.verified, r.verified, r.correct)).ToArray();
    }

    public static async Task<int> GetPendingCountAsync()
    {
        var (total, verified, _) = await ValutaBot.App.MiniApp.Data.Repositories.TradeRepository.GetOverallStatsAsync();
        return total - verified;
    }

    public static async Task<(string name, double agreeRatePct, double weight, int count)[]> GetSignalStatsAsync()
    {
        var votes = await ValutaBot.App.MiniApp.Data.Repositories.TradeRepository.GetAllSignalVotesAsync();
        return votes.Select(v =>
        {
            double agreeRate = v.verified > 0 ? (double)v.correct / v.verified : 0.5;
            double weight = Math.Clamp(agreeRate / 0.5, 0.2, 2.0); // simple calibration
            return (v.signalName, Math.Round(agreeRate * 100, 1), Math.Round(weight, 2), v.verified);
        }).OrderByDescending(s => s.Item2).ToArray();
    }

    public static double CalculateSignalWeight(System.Collections.Generic.IEnumerable<(string signalName, int correct, int verified)> votes, string signalName, double baseWeight = 1.0)
    {
        var v = System.Linq.Enumerable.FirstOrDefault(votes, x => x.signalName == signalName);
        if (v.verified < 5) return baseWeight;
        double agreeRate = (double)v.correct / v.verified;
        double adjustment = agreeRate / 0.5;
        return System.Math.Clamp(baseWeight * adjustment, 0.2, 2.0);
    }

    public static async Task<double> GetSignalWeightAsync(string signalName, double baseWeight = 1.0)
    {
        var votes = await ValutaBot.App.MiniApp.Data.Repositories.TradeRepository.GetAllSignalVotesAsync();
        return CalculateSignalWeight(votes, signalName, baseWeight);
    }

    // ── Background Verification ────────────────────────────────────────────

    public static async Task VerifyPendingAsync()
    {
        var now = DateTime.UtcNow;
        var toCheck = await ValutaBot.App.MiniApp.Data.Repositories.TradeRepository.GetPendingTradesToVerifyAsync(now);

        if (toCheck.Count == 0) return;

        Console.WriteLine($"[Tracker] Verifying {toCheck.Count} prediction(s)...");

        foreach (var record in toCheck)
        {
            // Drop predictions older than 24h that still can't be verified
            if ((now - record.CreatedAt).TotalHours > 24)
            {
                await ValutaBot.App.MiniApp.Data.Repositories.TradeRepository.DeletePendingTradeAsync(record.Id);
                continue;
            }

            double? exitPrice = await FetchExitPriceAsync(record);
            if (exitPrice == null || exitPrice <= 0)
                continue; // try again next cycle

            double priceDiff = (exitPrice.Value - record.EntryPrice) / record.EntryPrice;

            bool isSubMin = record.Timeframe.ToLower().StartsWith("s");
            double minMove = isSubMin ? 1e-8 : (record.IsForex ? 0.00002 : 0.00010);
            if (Math.Abs(priceDiff) < minMove)
            {
                await ValutaBot.App.MiniApp.Data.Repositories.TradeRepository.DeletePendingTradeAsync(record.Id);
                Console.WriteLine($"[Tracker] ~ {record.Asset}/{record.Timeframe} — flat market, discarded");
                continue;
            }

            if (record.Direction == "NEUTRAL")
            {
                await ValutaBot.App.MiniApp.Data.Repositories.TradeRepository.DeletePendingTradeAsync(record.Id);
                continue;
            }

            bool correct = record.Direction == "BUY" ? priceDiff > 0 : priceDiff < 0;
            record.ExitPrice  = exitPrice.Value;
            record.PnlBps     = Math.Round(priceDiff * 10000, 2);
            record.WasCorrect = correct;

            string winDirection = priceDiff > 0 ? "BUY" : "PUT";
            if (record.SourceDirections != null)
            {
                foreach (var kv in record.SourceDirections)
                {
                    if (kv.Value != "NEUTRAL")
                    {
                        bool wasSourceCorrect = kv.Value == winDirection;
                        await ValutaBot.App.MiniApp.Data.Repositories.TradeRepository.RecordSignalVoteAsync(kv.Key, wasSourceCorrect);
                    }
                }
            }

            _ = TradeOutcomeTracker.OnTradeVerifiedAsync(record);

            await ValutaBot.App.MiniApp.Data.Repositories.TradeRepository.DeletePendingTradeAsync(record.Id);

            string icon = correct ? "✅" : "❌";
            Console.WriteLine(
                $"[Tracker] {icon} {record.Asset}/{record.Timeframe} {record.Direction} " +
                $"entry={record.EntryPrice:F5} exit={exitPrice:F5} " +
                $"pnl={record.PnlBps:+0.0;-0.0} bps");
        }
    }

    private static async Task<double?> FetchExitPriceAsync(PredictionRecord record)
    {
        string sym = record.BinanceSymbol;

        // Fast path: Web Socket live prices (no allocations)
        if (BinanceWebSocketStream.TryGetLiveCandles(sym, "1m", out double[] wsPrices, out double[] wsVolumes, out int count) && count > 0) { var p = wsPrices[count - 1]; System.Buffers.ArrayPool<double>.Shared.Return(wsPrices); System.Buffers.ArrayPool<double>.Shared.Return(wsVolumes); return p; }
        {
            
        }

        // Fallback: Binance REST API (Historical Kline)
        if (!record.IsForex)
        {
            try
            {
                long endTime = new DateTimeOffset(record.VerifyAt).ToUnixTimeMilliseconds();
                double? binancePrice = await MarketDataFetcher.Instance.FetchHistoricalPriceAsync(sym, endTime);
                if (binancePrice.HasValue) return binancePrice;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Tracker] Binance historical kline fetch failed for {sym}: {ex.Message}");
            }
        }

        // 3. TwelveData REST API
        if (record.IsForex)
        {
            try
            {
                double? tdPrice = await TwelveDataService.FetchCurrentPriceAsync(record.Asset);
                if (tdPrice.HasValue) return tdPrice.Value;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Tracker] TwelveData fetch failed for {record.Asset}: {ex.Message}");
            }
        }

        return null;
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static string MapToBinanceSymbol(string asset) =>
        asset.ToUpper()
             .Replace("OTC", "")
             .Replace("/", "")
             .Replace(" ", "")
             .Replace("-", "")
             .Trim() switch
        {
            "EUR" or "EURUSD"  => "EURUSDT",
            "GBP" or "GBPUSD"  => "GBPUSDT",
            "AUD" or "AUDUSD"  => "AUDUSDT",
            "BTC" or "BITCOIN" => "BTCUSDT",
            "ETH"              => "ETHUSDT",
            "SOL"              => "SOLUSDT",
            var s when s.Length > 0 && !s.EndsWith("USDT") => s + "USDT",
            var s => s
        };

    // ── Data Types ─────────────────────────────────────────────────────────

    public class PredictionRecord
    {
        public string   Id            { get; set; } = "";
        public string   Direction     { get; set; } = "";
        public string   Asset         { get; set; } = "";
        public string   Timeframe     { get; set; } = "";
        public string   BinanceSymbol { get; set; } = "";
        public double   EntryPrice    { get; set; }
        public double?  ExitPrice     { get; set; }
        public double   PnlBps        { get; set; }
        public DateTime CreatedAt     { get; set; }
        public DateTime VerifyAt      { get; set; }
        public bool     IsForex       { get; set; }
        public bool?    WasCorrect    { get; set; }
        public Dictionary<string, string> SourceDirections { get; set; } = new();
    }

    public class AccuracyStats
    {
        public string Key { get; }
        public int Total { get; }
        public int Verified { get; }
        public int Correct { get; }
        public int Incorrect => Verified - Correct;
        public int Pending => Total - Verified;

        public AccuracyStats(string key, int total, int verified, int correct)
        {
            Key = key;
            Total = total;
            Verified = verified;
            Correct = correct;
        }

        public double WinRate => Verified > 0
            ? Math.Round((double)Correct / Verified * 100, 1)
            : 0;
        public bool HasData => Verified >= 5;

        public double CalibrationFactor => HasData
            ? Math.Clamp(WinRate / 50.0, 0.7, 1.3)
            : 1.0;
    }
}
