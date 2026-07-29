using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace ValutaBot.MiniApp;

/// <summary>
/// A zero-allocation Ring Buffer for storing real-time tick prices.
/// Avoids O(N) array cloning and GC pressure associated with ConcurrentQueue.
/// </summary>
public class TickRingBuffer
{
    private readonly double[] _prices;
    private int _head = 0;
    private int _count = 0;
    private readonly int _capacity;
    private readonly object _lock = new();

    public DateTime UpdatedAt { get; private set; }
    
    // Tracks the most recent price for fast O(1) reads
    public double LastPrice { get; private set; }

    public TickRingBuffer(int capacity = 100)
    {
        _capacity = capacity;
        _prices = new double[capacity];
    }

    public void AddTick(double price)
    {
        lock (_lock)
        {
            _prices[_head] = price;
            _head = (_head + 1) % _capacity;
            if (_count < _capacity) _count++;
            
            LastPrice = price;
            UpdatedAt = DateTime.UtcNow;
        }
    }

    public (double[] prices, int count) GetOrderedSnapshotRented(int count)
    {
        lock (_lock)
        {
            if (_count == 0) return (Array.Empty<double>(), 0);

            int takeCount = Math.Min(count, _count);
            double[] outPrices = ArrayPool<double>.Shared.Rent(takeCount);
            
            // Start index is the oldest element among the 'takeCount' newest elements
            int startIdx = (_head - takeCount + _capacity) % _capacity;
            for (int i = 0; i < takeCount; i++)
            {
                int srcIdx = (startIdx + i) % _capacity;
                outPrices[i] = _prices[srcIdx];
            }
            return (outPrices, takeCount);
        }
    }
}

public sealed class TwelveDataWebSocketStream : BackgroundService
{
    private static readonly ConcurrentDictionary<string, TickRingBuffer> _realtimeTicks = new();
    
    // Client broadcast dictionaries
    private static readonly ConcurrentDictionary<string, ConcurrentDictionary<string, WebSocket>> _clients = new();
    private static readonly ConcurrentDictionary<WebSocket, SemaphoreSlim> _clientLocks = new();
    private static ClientWebSocket? _activeWs;
    private static readonly object _wsLock = new();

    private record struct SocketPayload(byte[] Buffer, int Length);

    private static readonly Channel<SocketPayload> _jsonChannel = Channel.CreateBounded<SocketPayload>(new BoundedChannelOptions(2000)
    {
        SingleWriter = true,
        SingleReader = true,
        FullMode = BoundedChannelFullMode.DropOldest
    });

    /// <summary>
    /// Returns zero-latency in-memory tick prices from RAM (0.001s response time).
    /// Returns null if stream data for asset is not present or stale.
    /// </summary>
    public static bool TryGetRealtimePricesRented(string asset, out double[] prices, out int count, int reqCount = 30)
    {
        string cleanKey = NormalizeKey(asset);
        if (_realtimeTicks.TryGetValue(cleanKey, out var buffer))
        {
            if ((DateTime.UtcNow - buffer.UpdatedAt).TotalSeconds < 30)
            {
                var snap = buffer.GetOrderedSnapshotRented(reqCount);
                if (snap.count >= 10) 
                {
                    prices = snap.prices;
                    count = snap.count;
                    return true;
                }
                ArrayPool<double>.Shared.Return(snap.prices);
            }
        }
        prices = Array.Empty<double>();
        count = 0;
        return false;
    }
    
    // Used by MiniAppController for /api/stream/price
    public static double GetLastPrice(string asset)
    {
        string symbol = TwelveDataService.ConvertToTwelveSymbol(asset) ?? asset;
        string cleanKey = NormalizeKey(symbol);
        
        if (_realtimeTicks.TryGetValue(cleanKey, out var buffer))
        {
            if ((DateTime.UtcNow - buffer.UpdatedAt).TotalSeconds < 15)
            {
                return buffer.LastPrice;
            }
        }
        return 0;
    }
    
    public static (double price, DateTime timestamp)[] GetTicks(string asset)
    {
        string cleanKey = NormalizeKey(asset);
        if (_realtimeTicks.TryGetValue(cleanKey, out var buffer))
        {
            var snap = buffer.GetOrderedSnapshotRented(30);
            try
            {
                // Simulate timestamp for compatibility with old controller
                var result = new (double price, DateTime timestamp)[snap.count];
                for (int i = 0; i < snap.count; i++) result[i] = (snap.prices[i], DateTime.UtcNow.AddMilliseconds(-snap.count + i));
                return result;
            }
            finally
            {
                ArrayPool<double>.Shared.Return(snap.prices);
            }
        }
        return Array.Empty<(double price, DateTime timestamp)>();
    }

    public static async Task RegisterClientAsync(string asset, string clientId, WebSocket clientWs)
    {
        string symbol = TwelveDataService.ConvertToTwelveSymbol(asset) ?? asset;
        
        _clients.GetOrAdd(symbol, _ => new())[clientId] = clientWs;
        Console.WriteLine($"[TwelveData WS] Client {clientId} subscribed to {symbol}");

        await SubscribeToSymbolAsync(symbol);
        
        string cleanKey = NormalizeKey(symbol);
        if (_realtimeTicks.TryGetValue(cleanKey, out var buffer) && (DateTime.UtcNow - buffer.UpdatedAt).TotalSeconds < 15)
        {
            await SendToClientAsync(clientWs, symbol, buffer.LastPrice);
        }
    }

    public static void UnregisterClient(string asset, string clientId)
    {
        string symbol = TwelveDataService.ConvertToTwelveSymbol(asset) ?? asset;
        if (_clients.TryGetValue(symbol, out var dict))
        {
            if (dict.TryRemove(clientId, out var ws))
            {
                _clientLocks.TryRemove(ws, out _);
            }
        }
    }

    private static async Task SubscribeToSymbolAsync(string symbol)
    {
        ClientWebSocket? ws;
        lock (_wsLock) { ws = _activeWs; }
        
        if (ws == null || ws.State != WebSocketState.Open) return;

        try
        {
            var subMsg = new
            {
                action = "subscribe",
                @params = new
                {
                    symbols = symbol
                }
            };
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(subMsg);
            await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
            Console.WriteLine($"[TwelveData WS] Requested subscription for {symbol}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TwelveData WS] Subscription failed for {symbol}: {ex.Message}");
        }
    }

    public static Task StartBackgroundStreamingAsync()
    {
        // Now handled entirely by the background service lifecycle
        return Task.CompletedTask;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        Console.WriteLine("[TwelveData WS] Starting Unified Zero-Latency Forex Streaming Service...");

        // Launch consumer loop
        _ = Task.Run(() => BackgroundConsumerLoopAsync(ct), ct);

        var defaultSymbols = new[]
        {
            "EUR/USD", "GBP/USD", "AUD/USD", "USD/JPY", "USD/CAD", "USD/CHF",
            "NZD/USD", "EUR/GBP", "EUR/JPY", "GBP/JPY", "AUD/JPY", "CAD/JPY",
            "BTC/USD"
        };

        while (!ct.IsCancellationRequested)
        {
            string apiKey = TwelveDataService.GetApiKey();
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                Console.WriteLine("[TwelveData WS] No API key configured. Retrying in 10s...");
                await Task.Delay(10000, ct);
                continue;
            }

            try
            {
                string url = $"wss://ws.twelvedata.com/v1/quotes/price?apikey={apiKey}";
                using var ws = new ClientWebSocket();
                ws.Options.SetRequestHeader("User-Agent", "ValutaBot/2.0-ZeroAlloc");
                ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);

                await ws.ConnectAsync(new Uri(url), ct);
                Console.WriteLine("[TwelveData WS] Persistent Zero-Latency Stream Connected!");
                
                lock (_wsLock) { _activeWs = ws; }

                // Collect all symbols (defaults + any registered clients)
                var allSymbols = new HashSet<string>(defaultSymbols);
                foreach (var s in _clients.Keys) allSymbols.Add(s);

                var subMsgDto = new
                {
                    action = "subscribe",
                    @params = new
                    {
                        symbols = string.Join(",", allSymbols)
                    }
                };
                byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(subMsgDto);
                await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct);

                byte[] receiveBuffer = ArrayPool<byte>.Shared.Rent(65536);

                while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
                {
                    ValueWebSocketReceiveResult result = default;
                    int offset = 0;

                    try
                    {
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
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[TwelveData WS] Network error: {ex.Message}");
                        break;
                    }

                    if (ct.IsCancellationRequested || ws.State != WebSocketState.Open) break;

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
                Console.WriteLine($"[TwelveData WS] Exception: {ex.Message}. Reconnecting in 5s...");
            }
            finally
            {
                lock (_wsLock) { _activeWs = null; }
            }

            if (!ct.IsCancellationRequested)
            {
                try { await Task.Delay(5000, ct); } catch { }
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
                    ParseTick(payload.Buffer.AsSpan(0, payload.Length));
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(payload.Buffer);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Ignored on shutdown
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TwelveData Consumer] Error: {ex.Message}");
        }
        finally
        {
            while (_jsonChannel.Reader.TryRead(out var leftover))
            {
                ArrayPool<byte>.Shared.Return(leftover.Buffer);
            }
        }
    }

    private static void ParseTick(ReadOnlySpan<byte> jsonData)
    {
        try
        {
            var reader = new System.Text.Json.Utf8JsonReader(jsonData);
            string? ev = null;
            string? symbol = null;
            double price = 0;

            while (reader.Read())
            {
                if (reader.TokenType == System.Text.Json.JsonTokenType.PropertyName)
                {
                    if (reader.ValueTextEquals("event"u8))
                    {
                        reader.Read();
                        ev = reader.GetString();
                    }
                    else if (reader.ValueTextEquals("symbol"u8))
                    {
                        reader.Read();
                        symbol = reader.GetString();
                    }
                    else if (reader.ValueTextEquals("price"u8))
                    {
                        reader.Read();
                        if (reader.TokenType == System.Text.Json.JsonTokenType.String)
                            System.Buffers.Text.Utf8Parser.TryParse(reader.ValueSpan, out price, out _);
                        else
                            price = reader.GetDouble();
                    }
                }
            }

            if (ev == "price" && price > 0 && !string.IsNullOrEmpty(symbol))
            {
                string cleanKey = NormalizeKey(symbol);
                var buffer = _realtimeTicks.GetOrAdd(cleanKey, _ => new TickRingBuffer(100));
                buffer.AddTick(price);
                
                // Fire-and-forget broadcast to all connected frontend clients
                _ = BroadcastToClientsAsync(symbol, price);
            }
        }
        catch { /* skip malformed ticks */ }
    }

    private static async Task BroadcastToClientsAsync(string symbol, double price)
    {
        if (_clients.TryGetValue(symbol, out var dict))
        {
            var deadClients = new List<string>();
            foreach (var pair in dict)
            {
                var clientWs = pair.Value;
                if (clientWs.State == WebSocketState.Open)
                {
                    try
                    {
                        await SendToClientAsync(clientWs, symbol, price);
                    }
                    catch
                    {
                        deadClients.Add(pair.Key);
                    }
                }
                else
                {
                    deadClients.Add(pair.Key);
                }
            }

            foreach (var id in deadClients)
            {
                if (dict.TryRemove(id, out var deadWs))
                {
                    _clientLocks.TryRemove(deadWs, out _);
                    deadWs.Dispose();
                }
            }
        }
    }

    private struct ClientTickDto
    {
        [System.Text.Json.Serialization.JsonPropertyName("s")]
        public string Symbol { get; set; }
        
        [System.Text.Json.Serialization.JsonPropertyName("p")]
        public double Price { get; set; }
    }

    private static async Task SendToClientAsync(WebSocket ws, string symbol, double price)
    {
        var dto = new ClientTickDto { Symbol = symbol, Price = price };
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(dto);
        
        var lockObj = _clientLocks.GetOrAdd(ws, _ => new SemaphoreSlim(1, 1));
        await lockObj.WaitAsync();
        try
        {
            await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
        }
        finally
        {
            lockObj.Release();
        }
    }

    private static readonly ConcurrentDictionary<string, string> _keyCache = new();

    private static string NormalizeKey(string asset)
    {
        return AssetSanitizer.Sanitize(asset);
    }
}
