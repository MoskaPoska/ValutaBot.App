using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace ValutaBot.MiniApp;

public sealed class MarketDataService : BackgroundService
{
    private static readonly ConcurrentDictionary<string, (double price, double vol, DateTime time)> LatestPrices = new();
    private static readonly ConcurrentDictionary<string, double> LatestImbalance = new();
    private static readonly ConcurrentQueue<string> RecentAlerts = new();
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };

    public static double GetBookImbalance(string symbol)
    {
        return LatestImbalance.TryGetValue(symbol, out var val) ? val : 0;
    }

    public static Dictionary<string, object> GetLatestPrices()
    {
        return LatestPrices.ToDictionary(kv => kv.Key, kv => (object)new { price = kv.Value.price, change = kv.Value.vol, time = kv.Value.time.ToString("HH:mm:ss") });
    }

    public static List<string> GetRecentAlerts()
    {
        return RecentAlerts.Take(10).ToList();
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        Console.WriteLine("[MDS] MarketDataService started");

        // Start WebSocket + volume monitor concurrently
        await Task.WhenAll(
            RunBinanceWebSocket(ct),
            RunVolumeMonitor(ct)
        );
    }

    /* ─── Binance WebSocket ─── */

    private async Task RunBinanceWebSocket(CancellationToken ct)
    {
        var tickers = new[] { "btcusdt@ticker", "ethusdt@ticker", "bnbusdt@ticker", "solusdt@ticker", "xrpusdt@ticker", "adausdt@ticker", "dogeusdt@ticker", "dotusdt@ticker" };
        var books = new[] { "btcusdt@bookTicker", "ethusdt@bookTicker", "solusdt@bookTicker" };
        var allStreams = tickers.Concat(books).ToArray();
        string url = $"wss://stream.binance.com:9443/stream?streams={string.Join("/", allStreams)}";

        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var ws = new ClientWebSocket();
                await ws.ConnectAsync(new Uri(url), ct);
                Console.WriteLine("[MDS] WebSocket connected");

                var buffer = new byte[8192];

                while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
                {
                    var result = await ws.ReceiveAsync(buffer, ct);
                    if (result.MessageType != WebSocketMessageType.Text) continue;

                    var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    if (json.Contains("\"e\":\"bookTicker\"") || json.Contains("\"bookTicker\""))
                        ParseBookTicker(json);
                    else
                        ParseTicker(json);
                }
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                Console.WriteLine($"[MDS] WS error: {ex.Message}, reconnecting in 5s");
                await Task.Delay(5000, ct);
            }
        }
    }

    private static void ParseTicker(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var data = root.TryGetProperty("data", out var d) ? d : root;
            var symbol = data.GetProperty("s").GetString()!;
            var price = double.Parse(data.GetProperty("c").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
            var volume = double.Parse(data.GetProperty("v").GetString()!, System.Globalization.CultureInfo.InvariantCulture);

            var key = symbol.Replace("USDT", "/USDT");
            LatestPrices[key] = (price, volume, DateTime.UtcNow);
        }
        catch { /* malformed ticker, skip */ }
    }

    private static void ParseBookTicker(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var data = root.TryGetProperty("data", out var d) ? d : root;
            var symbol = data.GetProperty("s").GetString()!;
            var bidQty = double.Parse(data.GetProperty("B").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
            var askQty = double.Parse(data.GetProperty("A").GetString()!, System.Globalization.CultureInfo.InvariantCulture);

            double total = bidQty + askQty;
            double imbalance = total > 0 ? (bidQty - askQty) / total : 0;

            var key = symbol.Replace("USDT", "/USDT");
            LatestImbalance[key] = Math.Clamp(imbalance, -1, 1);
        }
        catch { /* malformed bookTicker, skip */ }
    }

    /* ─── Volume anomaly monitor ─── */

    private async Task RunVolumeMonitor(CancellationToken ct)
    {
        var symbols = new[] { "BTCUSDT", "ETHUSDT", "SOLUSDT" };

        while (!ct.IsCancellationRequested)
        {
            try
            {
                foreach (var sym in symbols)
                {
                    var (prices, volumes) = await FetchRecent(sym, ct);
                    if (volumes.Length < 20) continue;

                    double avgVol = volumes.Skip(volumes.Length - 20).Take(20).Average();
                    double lastVol = volumes[^1];
                    double ratio = lastVol / avgVol;

                    if (ratio > 3.0)
                    {
                        double change = (prices[^1] - prices[^2]) / prices[^2] * 100;
                        string dir = change >= 0 ? "\u2B06" : "\u2B07";
                        string alertMsg = $"\u26A0\uFE0F {sym.Replace("USDT", "/USDT")} | \u0420\u0435\u0437\u043A\u0438\u0439 \u0432\u0441\u043F\u043B\u0435\u0441\u043A \u043E\u0431\u044A\u0451\u043C\u0430: x{ratio:F1} ({dir} {Math.Abs(change):F2}%)";
                        Console.WriteLine($"[MDS] {alertMsg}");

                        RecentAlerts.Enqueue($"{DateTime.UtcNow:HH:mm:ss} {alertMsg}");
                        if (RecentAlerts.Count > 20) RecentAlerts.TryDequeue(out _);
                    }

                    // ─── Check prediction outcomes ───
                    CheckPredictionOutcomes(sym, prices[^1]);
                }
            }
            catch { /* retry next cycle */ }

            await Task.Delay(30_000, ct); // check every 30s
        }
    }

    private static async Task<(double[] prices, double[] volumes)> FetchRecent(string symbol, CancellationToken ct)
    {
        string url = $"https://api.binance.com/api/v3/klines?symbol={symbol}&interval=1m&limit=30";
        var response = await _http.GetStringAsync(url, ct);
        using var doc = JsonDocument.Parse(response);
        var arr = doc.RootElement.EnumerateArray().ToList();
        var prices = arr.Select(k => double.Parse(k[4].GetString()!, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        var volumes = arr.Select(k => double.Parse(k[5].GetString()!, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        return (prices, volumes);
    }

    private static double ComputeRsi(double[] data, int period)
    {
        if (data.Length < period + 1) return 50;
        int idx = data.Length - 1;
        double gain = 0, loss = 0;
        for (int i = idx - period + 1; i <= idx; i++)
        {
            double diff = data[i] - data[i - 1];
            if (diff > 0) gain += diff; else loss -= diff;
        }
        double avgGain = gain / period;
        double avgLoss = loss / period;
        if (avgLoss < 1e-12) return 100;
        return 100 - 100 / (1 + avgGain / avgLoss);
    }

    private static void CheckPredictionOutcomes(string symbol, double currentPrice)
    {
        try
        {
            string asset = symbol.Replace("USDT", "/USDT");
            var pending = SignalTracker.GetPending();
            foreach (var pred in pending)
            {
                if (pred.Asset != asset) continue;
                double elapsed = (DateTime.UtcNow - pred.CreatedAt).TotalMinutes;
                if (elapsed < 1.5) continue;

                double change = (currentPrice - pred.Price) / pred.Price;
                bool correct = pred.Direction == "BUY" ? change > 0.001 : change < -0.001;
                SignalTracker.MarkChecked(pred, correct);
                Console.WriteLine($"[Tracker] {pred.Asset} pred={pred.Direction} actual={(change > 0 ? "UP" : "DOWN")} ({(correct ? "CORRECT" : "WRONG")}) acc={SignalTracker.GetOverallAccuracy()}%");
            }
        }
        catch { }
    }
}
