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

public sealed class MarketDataService : BackgroundService
{
    private static readonly ConcurrentDictionary<string, (double price, double vol, DateTime time)> LatestPrices = new();
    private static readonly ConcurrentQueue<string> RecentAlerts = new();

    private record struct SocketPayload(byte[] Buffer, int Length);

    private static readonly Channel<SocketPayload> _jsonChannel = Channel.CreateBounded<SocketPayload>(new BoundedChannelOptions(2000)
    {
        SingleWriter = true,
        SingleReader = true,
        FullMode = BoundedChannelFullMode.DropOldest
    });

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
        Console.WriteLine("[MDS] MarketDataService started (Zero-Allocation Architecture)");

        // Start consumer loop
        _ = Task.Run(() => ConsumerLoopAsync(ct), ct);

        // Start WebSocket network loop
        await RunBinanceWebSocket(ct);
    }

    private async Task RunBinanceWebSocket(CancellationToken ct)
    {
        var tickers = new[] { "btcusdt@ticker", "ethusdt@ticker", "bnbusdt@ticker", "solusdt@ticker", "xrpusdt@ticker", "adausdt@ticker", "dogeusdt@ticker", "dotusdt@ticker" };
        string url = $"wss://stream.binance.com:9443/stream?streams={string.Join("/", tickers)}";

        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var ws = new ClientWebSocket();
                ws.Options.SetRequestHeader("User-Agent", "ValutaBot/2.0-ZeroAlloc");
                ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);
                await ws.ConnectAsync(new Uri(url), ct);
                Console.WriteLine("[MDS] Binance Ticker WebSocket connected");

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
                Console.WriteLine($"[MDS] WS error: {ex.Message}, reconnecting in 5s");
            }


            if (!ct.IsCancellationRequested)
            {
                try { await Task.Delay(5000, ct); } catch { }
            }
        }
    }

    private static async Task ConsumerLoopAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var payload in _jsonChannel.Reader.ReadAllAsync(ct))
            {
                try
                {
                    ParseMessage(payload.Buffer.AsSpan(0, payload.Length));
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
            Console.WriteLine($"[MDS Consumer] Error: {ex.Message}");
        }
        finally
        {
            while (_jsonChannel.Reader.TryRead(out var leftover))
            {
                ArrayPool<byte>.Shared.Return(leftover.Buffer);
            }
        }
    }

    private static void ParseMessage(ReadOnlySpan<byte> jsonData)
    {
        try
        {
            var dto = System.Text.Json.JsonSerializer.Deserialize<BinanceStreamEventDto>(jsonData);

            if (dto.Data.EventType == "24hrTicker" && !string.IsNullOrEmpty(dto.Data.Symbol) && dto.Data.ClosePrice > 0)
            {
                var cleanSymbol = ValutaBot.MiniApp.AssetSanitizer.Sanitize(dto.Data.Symbol);
                var displayKey = dto.Data.Symbol.ToUpper().Replace("USDT", "/USDT"); // for display dictionary
                
                LatestPrices[displayKey] = (dto.Data.ClosePrice, dto.Data.Volume, DateTime.UtcNow);
                
                CheckVolumeAnomaly(cleanSymbol, dto.Data.ClosePrice, dto.Data.Volume);
                SignalTracker.UpdatePrice(cleanSymbol, dto.Data.ClosePrice);
            }
        }
        catch { /* skip malformed ticks */ }
    }

    private struct BinanceStreamEventDto
    {
        [System.Text.Json.Serialization.JsonPropertyName("data")]
        public Binance24hrTickerDto Data { get; set; }
    }

    private struct Binance24hrTickerDto
    {
        [System.Text.Json.Serialization.JsonPropertyName("e")]
        public string EventType { get; set; }
        
        [System.Text.Json.Serialization.JsonPropertyName("s")]
        public string Symbol { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("c")]
        [System.Text.Json.Serialization.JsonNumberHandling(System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString)]
        public double ClosePrice { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("v")]
        [System.Text.Json.Serialization.JsonNumberHandling(System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString)]
        public double Volume { get; set; }
    }

    private static readonly ConcurrentDictionary<string, TickRingBuffer> _volumeHistory = new();
    
    private static void CheckVolumeAnomaly(string symbol, double price, double volume24h)
    {
        // For accurate tracking we need to observe rapid increases in volume.
        // We can use our existing TickRingBuffer to store the last 20 ticks of 24h volume.
        if (symbol == "BTCUSDT" || symbol == "ETHUSDT" || symbol == "SOLUSDT")
        {
            var buffer = _volumeHistory.GetOrAdd(symbol, _ => new TickRingBuffer(20));
            var oldSnapshot = buffer.GetOrderedSnapshot(20);
            
            buffer.AddTick(volume24h);
            
            if (oldSnapshot.Length >= 20)
            {
                double avgVol = 0;
                for (int i = 0; i < oldSnapshot.Length; i++) avgVol += oldSnapshot[i];
                avgVol /= oldSnapshot.Length;
                
                // If 24h volume suddenly increases relative to its running average
                // (Note: since 24h volume is cumulative, a massive jump means a huge burst of volume just occurred)
                if (avgVol > 0)
                {
                    double ratio = volume24h / avgVol;
                    if (ratio > 1.05) // 5% jump in 24h volume in a few seconds is massive
                    {
                        // To prevent spam, only alert if we haven't alerted recently
                        string alertKey = $"{symbol}_alert_cooldown";
                        if (!LatestPrices.ContainsKey(alertKey) || (DateTime.UtcNow - LatestPrices[alertKey].time).TotalMinutes > 5)
                        {
                            string alertMsg = $"\u26A0\uFE0F {symbol.Replace("USDT", "/USDT")} | РИзкий всплеск объёма: +{(ratio - 1) * 100:F1}%";
                            Console.WriteLine($"[MDS] {alertMsg}");

                            RecentAlerts.Enqueue($"{DateTime.UtcNow:HH:mm:ss} {alertMsg}");
                            if (RecentAlerts.Count > 20) RecentAlerts.TryDequeue(out _);
                            
                            LatestPrices[alertKey] = (0, 0, DateTime.UtcNow);
                        }
                    }
                }
            }
        }
    }
}
