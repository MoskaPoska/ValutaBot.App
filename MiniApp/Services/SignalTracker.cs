using System.Collections.Concurrent;
using System.Net.Http;
using System.Text.Json;

namespace ValutaBot.MiniApp;

/// <summary>
/// Tracks prediction signals and automatically verifies them after the candle expires.
/// Provides per-asset, per-timeframe, and per-source win rate statistics.
/// </summary>
public static class SignalTracker
{
    // ── Storage ────────────────────────────────────────────────────────────
    private static readonly ConcurrentDictionary<string, ConcurrentQueue<bool>> _signalHistory = new();
    private static readonly ConcurrentDictionary<string, PredictionRecord> _pending = new();
    private static readonly ConcurrentQueue<PredictionRecord> _archive = new();
    private static readonly ConcurrentDictionary<string, AccuracyStats> _stats = new();

    // Live price cache — updated by MiniAppController after each analysis
    private static readonly ConcurrentDictionary<string, (double price, DateTime at)> _priceCache = new();

    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(5) };
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
    public static void RecordPrediction(
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

        _pending[record.Id] = record;

        // Pre-count total for "ALL" and per-key (verified will be filled later)
        GetOrCreate("ALL").IncrementTotal();
        GetOrCreate($"{asset}_{timeframe}").IncrementTotal();

        Console.WriteLine($"[Tracker] Recorded {direction} {asset}/{timeframe} @ {price:F5} " +
                          $"— verify in {verifyDelaySecs}s");
    }

    /// <summary>Call this after each analysis to keep the price cache fresh for verification.</summary>
    public static void UpdatePrice(string binanceSymbol, double price)
    {
        _priceCache[binanceSymbol.ToUpper()] = (price, DateTime.UtcNow);
    }

    /// <summary>Records whether a sub-signal agreed with the final decision (for adaptive weighting).</summary>
    public static void RecordSignalVote(string signalName, bool agreedWithFinal)
    {
        var q = _signalHistory.GetOrAdd(signalName, _ => new ConcurrentQueue<bool>());
        q.Enqueue(agreedWithFinal);
        while (q.Count > 200) q.TryDequeue(out _);
    }

    // ── Public Read API ────────────────────────────────────────────────────

    public static AccuracyStats GetOverallStats() =>
        _stats.TryGetValue("ALL", out var s) ? s : new AccuracyStats();

    public static AccuracyStats GetStats(string asset, string timeframe) =>
        _stats.TryGetValue($"{asset}_{timeframe}", out var s) ? s : new AccuracyStats();

    public static AccuracyStats[] GetAllStats() => _stats.Values.ToArray();

    public static PredictionRecord[] GetRecentArchive(int count = 30) =>
        _archive.Reverse().Take(count).ToArray();

    public static int GetPendingCount() => _pending.Count;

    public static (string name, double agreeRatePct, double weight, int count)[] GetSignalStats()
    {
        return _signalHistory.Select(kv =>
        {
            var arr = kv.Value.ToArray();
            double agreeRate = arr.Length > 0
                ? arr.Count(v => v) / (double)arr.Length
                : 0.5;
            return (kv.Key,
                    Math.Round(agreeRate * 100, 1),
                    Math.Round(GetSignalWeight(kv.Key), 2),
                    arr.Length);
        }).OrderByDescending(s => s.Item2).ToArray();
    }

    /// <summary>
    /// Returns adaptive weight for a signal source based on its historical agreement rate.
    /// Used by MiniAppController to boost reliable sources and downweight noisy ones.
    /// </summary>
    public static double GetSignalWeight(string signalName, double baseWeight = 1.0)
    {
        if (!_signalHistory.TryGetValue(signalName, out var q) || q.Count < 5)
            return baseWeight;

        var arr = q.ToArray();
        double winRate = arr.Count(v => v) / (double)arr.Length;
        // winRate 0.50 (50%) → weight = baseWeight (neutral)
        // winRate 0.80 (80%) → weight = baseWeight × 1.6 (boosted)
        // winRate 0.30 (30%) → weight = baseWeight × 0.6 (suppressed)
        double adjustment = winRate / 0.5;
        return Math.Clamp(baseWeight * adjustment, 0.2, 2.0);
    }

    // ── Background Verification ────────────────────────────────────────────

    private static async Task VerifyPendingAsync()
    {
        var now = DateTime.UtcNow;
        var toCheck = _pending.Values
            .Where(r => r.VerifyAt <= now)
            .OrderBy(r => r.VerifyAt)
            .ToList();

        if (toCheck.Count == 0) return;

        Console.WriteLine($"[Tracker] Verifying {toCheck.Count} prediction(s)...");

        foreach (var record in toCheck)
        {
            // Drop predictions older than 24h that still can't be verified
            if ((now - record.CreatedAt).TotalHours > 24)
            {
                _pending.TryRemove(record.Id, out _);
                continue;
            }

            double? exitPrice = await FetchExitPriceAsync(record);
            if (exitPrice == null || exitPrice <= 0)
                continue; // try again next cycle

            double priceDiff = (exitPrice.Value - record.EntryPrice) / record.EntryPrice;

            // Ignore flat markets — too noisy to count
            double minMove = record.IsForex ? 0.00010 : 0.00080; // 1 pip forex, ~8 bps crypto
            if (Math.Abs(priceDiff) < minMove)
            {
                _pending.TryRemove(record.Id, out _); // discard, not counted
                Console.WriteLine($"[Tracker] ~ {record.Asset}/{record.Timeframe} — flat market, discarded");
                continue;
            }

            if (record.Direction == "NEUTRAL")
            {
                _pending.TryRemove(record.Id, out _);
                continue;
            }

            bool correct = record.Direction == "BUY" ? priceDiff > 0 : priceDiff < 0;
            record.ExitPrice  = exitPrice.Value;
            record.PnlBps     = Math.Round(priceDiff * 10000, 2);
            record.WasCorrect = correct;

            // Evaluate sub-signal accuracy based on the actual winning direction
            string winDirection = priceDiff > 0 ? "BUY" : "PUT";
            if (record.SourceDirections != null)
            {
                foreach (var kv in record.SourceDirections)
                {
                    if (kv.Value != "NEUTRAL")
                    {
                        bool wasSourceCorrect = kv.Value == winDirection;
                        RecordSignalVote(kv.Key, wasSourceCorrect);
                    }
                }
            }

            // Update accuracy stats
            GetOrCreate("ALL").Record(correct);
            GetOrCreate($"{record.Asset}_{record.Timeframe}").Record(correct);

            // Archive
            _pending.TryRemove(record.Id, out _);
            _archive.Enqueue(record);
            while (_archive.Count > 500) _archive.TryDequeue(out _);

            string icon = correct ? "✅" : "❌";
            Console.WriteLine(
                $"[Tracker] {icon} {record.Asset}/{record.Timeframe} {record.Direction} " +
                $"entry={record.EntryPrice:F5} exit={exitPrice:F5} " +
                $"pnl={record.PnlBps:+0.0;-0.0} bps");
        }

        // Print summary every 10 verified signals
        var all = GetOverallStats();
        if (all.Verified > 0 && all.Verified % 10 == 0)
        {
            Console.WriteLine(
                $"[Tracker] ── Win Rate: {all.WinRate:F1}% " +
                $"({all.Correct}✅ / {all.Incorrect}❌ / {all.Verified} verified) ──");
        }
    }

    private static async Task<double?> FetchExitPriceAsync(PredictionRecord record)
    {
        string sym = record.BinanceSymbol;

        // 1. Live cache (updated by MiniAppController after each analysis)
        if (_priceCache.TryGetValue(sym, out var cached) &&
            (DateTime.UtcNow - cached.at).TotalMinutes < 5)
        {
            return cached.price;
        }

        // 2. Binance REST API for crypto
        if (!record.IsForex)
        {
            try
            {
                var json = await _http.GetStringAsync(
                    $"https://api.binance.com/api/v3/ticker/price?symbol={sym}");
                var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("price", out var p))
                    return double.Parse(p.GetString()!, System.Globalization.CultureInfo.InvariantCulture);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Tracker] Binance price fetch failed for {sym}: {ex.Message}");
            }
        }

        // 3. Stale cache (up to 15 min old) — better than nothing for forex
        if (_priceCache.TryGetValue(sym, out var stale) &&
            (DateTime.UtcNow - stale.at).TotalMinutes < 15)
        {
            return stale.price;
        }

        return null;
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static AccuracyStats GetOrCreate(string key) =>
        _stats.GetOrAdd(key, _ => new AccuracyStats(key));

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
        private int _total, _verified, _correct;

        public string Key { get; }

        public AccuracyStats(string key = "ALL") => Key = key;

        public void IncrementTotal()  => Interlocked.Increment(ref _total);
        public void Record(bool correct)
        {
            Interlocked.Increment(ref _verified);
            if (correct) Interlocked.Increment(ref _correct);
        }

        public int    Total     => _total;
        public int    Verified  => _verified;
        public int    Correct   => _correct;
        public int    Incorrect => _verified - _correct;
        public int    Pending   => _total - _verified;
        public double WinRate   => _verified > 0
            ? Math.Round((double)_correct / _verified * 100, 1)
            : 0;
        public bool   HasData   => _verified >= 5;

        /// <summary>Calibrated probability boost based on win rate (used in sniper decision).</summary>
        public double CalibrationFactor => HasData
            ? Math.Clamp(WinRate / 50.0, 0.7, 1.3)
            : 1.0;
    }
}
