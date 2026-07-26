using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace ValutaBot.MiniApp;

public sealed class LiquidationHeatmapService : BackgroundService
{
    private static readonly ConcurrentDictionary<string, ConcurrentDictionary<double, LiquidationBucket>> _heatmap = new();

    private record struct SocketPayload(byte[] Buffer, int Length);

    private static readonly Channel<SocketPayload> _jsonChannel = Channel.CreateBounded<SocketPayload>(new BoundedChannelOptions(2000)
    {
        SingleWriter = true,
        SingleReader = true,
        FullMode = BoundedChannelFullMode.DropOldest
    });

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
        
        // Start consumer loop for JSON processing
        _ = Task.Run(() => BackgroundConsumerLoopAsync(ct), ct);

        var symbols = new[] { "btcusdt", "ethusdt", "solusdt" };
        var streams = symbols.Select(s => $"{s}@forceOrder").ToList();

        while (!ct.IsCancellationRequested)
        {
            ClientWebSocket? ws = null;
            try
            {
                string url = $"wss://fstream.binance.com/ws";
                ws = new ClientWebSocket();
                ws.Options.SetRequestHeader("User-Agent", "ValutaBot/2.0-ZeroAlloc");
                ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);
                
                await ws.ConnectAsync(new Uri(url), ct);
                Console.WriteLine("[LIQ] Futures WS connected");

                var subMsg = JsonSerializer.Serialize(new
                {
                    method = "SUBSCRIBE",
                    @params = streams,
                    id = 1
                });
                await ws.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(subMsg)), WebSocketMessageType.Text, true, ct);

                byte[] receiveBuffer = ArrayPool<byte>.Shared.Rent(65536);

                while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
                {
                    ValueWebSocketReceiveResult result = default;
                    int offset = 0;

                    do
                    {
                        if (offset >= receiveBuffer.Length)
                        {
                            var newBuffer = ArrayPool<byte>.Shared.Rent(receiveBuffer.Length * 2);
                            Array.Copy(receiveBuffer, newBuffer, offset);
                            ArrayPool<byte>.Shared.Return(receiveBuffer);
                            receiveBuffer = newBuffer;
                        }
                        
                        result = await ws.ReceiveAsync(receiveBuffer.AsMemory(offset, receiveBuffer.Length - offset), ct);
                        if (result.MessageType == WebSocketMessageType.Close) break;

                        offset += result.Count;
                    }
                    while (!result.EndOfMessage && !ct.IsCancellationRequested);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        break;
                    }

                    if (offset > 0)
                    {
                        byte[] channelBuffer = ArrayPool<byte>.Shared.Rent(offset);
                        Array.Copy(receiveBuffer, channelBuffer, offset);

                        if (!_jsonChannel.Writer.TryWrite(new SocketPayload(channelBuffer, offset)))
                        {
                            ArrayPool<byte>.Shared.Return(channelBuffer);
                        }
                    }
                }
                
                ArrayPool<byte>.Shared.Return(receiveBuffer);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                Console.WriteLine($"[LIQ] WS error: {ex.Message}, reconnecting in 5s");
                await Task.Delay(5000, ct);
            }
            finally
            {
                try { ws?.Dispose(); } catch { }
            }
        }
    }

    private static async Task BackgroundConsumerLoopAsync(CancellationToken token)
    {
        try
        {
            await foreach (var payload in _jsonChannel.Reader.ReadAllAsync(token))
            {
                try
                {
                    ParseLiquidation(payload.Buffer.AsSpan(0, payload.Length));
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(payload.Buffer);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Console.WriteLine($"[Heatmap Consumer] Error: {ex.Message}");
        }
        finally
        {
            while (_jsonChannel.Reader.TryRead(out var leftover))
            {
                ArrayPool<byte>.Shared.Return(leftover.Buffer);
            }
        }
    }

    private static readonly ConcurrentDictionary<string, string> _liqKeyCache = new();

    private static string NormalizeLiqKey(string symbol)
    {
        if (_liqKeyCache.TryGetValue(symbol, out var cached)) return cached;
        var clean = symbol.Replace("USDT", "/USDT");
        _liqKeyCache[symbol] = clean;
        return clean;
    }

    private static void ParseLiquidation(ReadOnlySpan<byte> jsonData)
    {
        try
        {
            var readerOptions = new JsonReaderOptions { CommentHandling = JsonCommentHandling.Skip };
            var reader = new Utf8JsonReader(jsonData, readerOptions);

            using var doc = JsonDocument.ParseValue(ref reader);
            var root = doc.RootElement;

            if (!root.TryGetProperty("o", out var o)) return;

            var symbol = o.TryGetProperty("s", out var sProp) ? (sProp.GetString() ?? "") : "";
            var side = o.TryGetProperty("S", out var SProp) ? (SProp.GetString() ?? "") : "";
            
            if (!o.TryGetProperty("p", out var pProp) || !o.TryGetProperty("q", out var qProp)) return;
            
            string? pStr = pProp.GetString();
            string? qStr = qProp.GetString();
            
            if (string.IsNullOrEmpty(pStr) || string.IsNullOrEmpty(qStr)) return;

            if (!double.TryParse(pStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double price) ||
                !double.TryParse(qStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double qty))
                return;

            string key = NormalizeLiqKey(symbol);
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
