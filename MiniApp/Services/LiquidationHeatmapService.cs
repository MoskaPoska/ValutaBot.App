using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace ValutaBot.MiniApp;

public sealed class LiquidationHeatmapService : BackgroundService
{
    private static readonly ConcurrentDictionary<string, ConcurrentDictionary<double, LiquidationBucket>> _heatmap = new();

    public static object GetHeatmapData()
    {
        var result = new Dictionary<string, object>();
        foreach (var kv in _heatmap)
        {
            var levels = kv.Value
                .OrderByDescending(b => b.Key)
                .Select(b =>
                {
                    double longVol, shortVol;
                    lock (b.Value)
                    {
                        longVol = b.Value.LongVolume;
                        shortVol = b.Value.ShortVolume;
                    }
                    return new
                    {
                        price = Math.Round(b.Key, 2),
                        longVol = Math.Round(longVol, 4),
                        shortVol = Math.Round(shortVol, 4),
                        total = Math.Round(longVol + shortVol, 4)
                    };
                })
                .ToList();

            if (levels.Count > 0)
                result[kv.Key] = levels;
        }
        return result;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        Console.WriteLine("[LIQ] LiquidationHeatmapService started");
        
        // Start background cleanup loop
        _ = RunCleanupLoopAsync(ct);

        var symbols = new[] { "btcusdt", "ethusdt", "solusdt" };
        var streams = symbols.Select(s => $"{s}@forceOrder").ToList();

        while (!ct.IsCancellationRequested)
        {
            try
            {
                string url = $"wss://fstream.binance.com/ws";
                using var ws = new ClientWebSocket();
                await ws.ConnectAsync(new Uri(url), ct);
                Console.WriteLine("[LIQ] Futures WS connected");

                var subMsg = JsonSerializer.Serialize(new
                {
                    method = "SUBSCRIBE",
                    @params = streams,
                    id = 1
                });
                await ws.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(subMsg)), WebSocketMessageType.Text, true, ct);

                var buffer = new byte[4096];

                while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
                {
                    var result = await ws.ReceiveAsync(buffer, ct);
                    if (result.MessageType != WebSocketMessageType.Text) continue;

                    var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    ParseLiquidation(json);
                }
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                Console.WriteLine($"[LIQ] WS error: {ex.Message}, reconnecting in 5s");
                await Task.Delay(5000, ct);
            }
        }
    }

    private static void ParseLiquidation(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("o", out var o)) return;

            var symbol = o.GetProperty("s").GetString() ?? "";
            var side = o.GetProperty("S").GetString() ?? "";
            var price = double.Parse(o.GetProperty("p").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
            var qty = double.Parse(o.GetProperty("q").GetString()!, System.Globalization.CultureInfo.InvariantCulture);

            string key = symbol.Replace("USDT", "/USDT");
            double bucketSize = price > 10000 ? 100 : price > 1000 ? 10 : price > 100 ? 1 : 0.5;
            double bucket = Math.Round(price / bucketSize) * bucketSize;
            double usdValue = price * qty;

            var buckets = _heatmap.GetOrAdd(key, _ => new ConcurrentDictionary<double, LiquidationBucket>());

            var liqBucket = buckets.GetOrAdd(bucket, _ => new LiquidationBucket());
            lock (liqBucket)
            {
                liqBucket.Price = bucket;
                liqBucket.LastSeen = DateTime.UtcNow;
                if (side == "SELL")
                    liqBucket.ShortVolume += usdValue;
                else
                    liqBucket.LongVolume += usdValue;
            }
        }
        catch { /* malformed liquidation, skip */ }
    }

    private class LiquidationBucket
    {
        public double Price { get; set; }
        public double LongVolume { get; set; }
        public double ShortVolume { get; set; }
        public DateTime LastSeen { get; set; } = DateTime.UtcNow;
    }

    private async Task RunCleanupLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(2), ct);
                
                var cutoff = DateTime.UtcNow.AddMinutes(-30);
                foreach (var kv in _heatmap)
                {
                    var buckets = kv.Value;
                    foreach (var b in buckets)
                    {
                        if (b.Value.LastSeen < cutoff)
                        {
                            buckets.TryRemove(b.Key, out _);
                        }
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Console.WriteLine($"[LIQ] Cleanup error: {ex.Message}");
            }
        }
    }
}
