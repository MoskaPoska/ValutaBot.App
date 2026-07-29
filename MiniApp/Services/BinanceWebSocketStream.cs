using System.Buffers;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace ValutaBot.MiniApp;

/// <summary>
/// A zero-allocation (on the hot path) Ring Buffer for storing candlestick data.
/// Avoids O(N) array cloning on every WebSocket tick.
/// </summary>
public class CandleSeriesBuffer
{
    private readonly double[] _prices;
    private readonly double[] _volumes;
    private int _head = 0;
    private int _count = 0;
    private readonly int _capacity;
    private readonly object _lock = new();

    public long LastCandleTime { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    public CandleSeriesBuffer(int capacity = 100)
    {
        _capacity = capacity;
        _prices = new double[capacity];
        _volumes = new double[capacity];
    }

    public void Update(double price, double volume, long candleTime)
    {
        lock (_lock)
        {
            if (LastCandleTime == candleTime && _count > 0)
            {
                // Update latest tick (same candle)
                int idx = (_head - 1 + _capacity) % _capacity;
                _prices[idx] = price;
                _volumes[idx] = volume;
            }
            else
            {
                // Push new candle
                _prices[_head] = price;
                _volumes[_head] = volume;
                _head = (_head + 1) % _capacity;
                if (_count < _capacity) _count++;
            }
            LastCandleTime = candleTime;
            UpdatedAt = DateTime.UtcNow;
        }
    }

    public (double[] prices, double[] volumes, int count) GetOrderedSnapshotRented()
    {
        lock (_lock)
        {
            if (_count == 0) return (Array.Empty<double>(), Array.Empty<double>(), 0);

            double[] outPrices = ArrayPool<double>.Shared.Rent(_count);
            double[] outVolumes = ArrayPool<double>.Shared.Rent(_count);

            int startIdx = (_head - _count + _capacity) % _capacity;
            for (int i = 0; i < _count; i++)
            {
                int srcIdx = (startIdx + i) % _capacity;
                outPrices[i] = _prices[srcIdx];
                outVolumes[i] = _volumes[srcIdx];
            }
            return (outPrices, outVolumes, _count);
        }
    }
}

public static class BinanceWebSocketStream
{
    private static readonly ConcurrentDictionary<string, CandleSeriesBuffer> _liveCandles = new();
    
    // Migrated from MarketDataService for zero-redundancy
    private static readonly ConcurrentDictionary<string, (double price, double vol, DateTime time)> LatestPrices = new();
    private static readonly ConcurrentQueue<string> RecentAlerts = new();
    private static readonly ConcurrentDictionary<string, TickRingBuffer> _volumeHistory = new();

    public static object GetLatestPrices()
    {
        var result = new System.Collections.Generic.Dictionary<string, object>();
        foreach (var kv in LatestPrices)
        {
            if (!kv.Key.Contains("_alert_cooldown"))
            {
                result[kv.Key] = new { price = kv.Value.price, volume = kv.Value.vol };
            }
        }
        return result;
    }

    public static System.Collections.Generic.List<string> GetRecentAlerts()
    {
        return RecentAlerts.ToArray().Reverse().ToList();
    }

    public record OrderbookDepthSnapshot(
        double TotalBidVolume,
        double TotalAskVolume,
        double ImbalanceRatio, // Range -1.0 to +1.0
        DateTime UpdatedAt
    );

    private static readonly ConcurrentDictionary<string, OrderbookDepthSnapshot> _liveOrderbooks = new();

    // Payload consists of rented ArrayPool array and the valid length.
    private record struct SocketPayload(byte[] Buffer, int Length);

    private static Channel<SocketPayload> _jsonChannel = CreateChannel();

    private static Channel<SocketPayload> CreateChannel() => Channel.CreateBounded<SocketPayload>(new BoundedChannelOptions(2000)
    {
        SingleWriter = true,
        SingleReader = true,
        FullMode = BoundedChannelFullMode.DropOldest
    });

    private static CancellationTokenSource? _cts;
    private static bool _isRunning = false;

    public static bool TryGetLiveCandles(string symbol, string interval, out double[] prices, out double[] volumes, out int count)
    {
        string key = $"{symbol.ToUpper()}_{interval.ToLower()}";
        if (_liveCandles.TryGetValue(key, out var buffer))
        {
            if ((DateTime.UtcNow - buffer.UpdatedAt).TotalSeconds < 5)
            {
                (prices, volumes, count) = buffer.GetOrderedSnapshotRented();
                return true;
            }
        }

        prices = Array.Empty<double>();
        volumes = Array.Empty<double>();
        count = 0;
        return false;
    }

    public static bool TryGetLiveOrderbookImbalance(string symbol, out OrderbookDepthSnapshot? snapshot)
    {
        string key = symbol.ToUpper();
        if (_liveOrderbooks.TryGetValue(key, out var data) && (DateTime.UtcNow - data.UpdatedAt).TotalSeconds < 5)
        {
            snapshot = data;
            return true;
        }
        snapshot = null;
        return false;
    }

    public static void StartStream(IEnumerable<string> symbols, string interval = "1m")
    {
        if (_isRunning) return;
        _isRunning = true;
        _cts = new CancellationTokenSource();

        var streams = new List<string>();
        foreach (var s in symbols)
        {
            string cleanSym = AssetSanitizer.Sanitize(s).ToLower();
            streams.Add($"{cleanSym}@kline_{interval}");
            streams.Add($"{cleanSym}@depth20@100ms");
        }
        string streamNames = string.Join("/", streams);
        string wsUrl = $"wss://stream.binance.com:9443/stream?streams={streamNames}";

        _jsonChannel = CreateChannel();

        _ = Task.Run(() => BackgroundConsumerLoopAsync(interval, _cts.Token));
        _ = Task.Run(() => ProducerNetworkLoopAsync(wsUrl, _cts.Token));
    }

    public static void StopStream()
    {
        if (!_isRunning) return;
        _cts?.Cancel();
        _jsonChannel.Writer.TryComplete();
        _isRunning = false;
        BotLogger.Info("[WebSocket Producer] WebSocket stream stopped and disconnected.");
    }
    
    public static void Stop() => StopStream(); // Forward to StopStream for backwards compatibility

    private static async Task ProducerNetworkLoopAsync(string url, CancellationToken token)
    {
        int reconnectAttempts = 0;

        while (!token.IsCancellationRequested)
        {
            try
            {
                using var client = new ClientWebSocket();
                client.Options.SetRequestHeader("User-Agent", "ValutaBot/2.0-ZeroAlloc");
                client.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);

                BotLogger.Info($"[WebSocket Producer] Connecting to Binance real-time stream: {url}");
                await client.ConnectAsync(new Uri(url), token);
                reconnectAttempts = 0;
                BotLogger.Info("[WebSocket Producer] Connected successfully to Binance WebSocket stream!");

                byte[] receiveBuffer = ArrayPool<byte>.Shared.Rent(65536);

                while (client.State == WebSocketState.Open && !token.IsCancellationRequested)
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

                            result = await client.ReceiveAsync(receiveBuffer.AsMemory(offset, receiveBuffer.Length - offset), token);
                            if (result.MessageType == WebSocketMessageType.Close) break;
                            offset += result.Count;
                        }
                        while (!result.EndOfMessage && !token.IsCancellationRequested);
                    }
                    catch (WebSocketException wsEx)
                    {
                        BotLogger.Warn($"[WebSocket Producer] Network frame receive error: {wsEx.Message}. Reconnecting.");
                        break;
                    }

                    if (token.IsCancellationRequested || client.State != WebSocketState.Open)
                    {
                        BotLogger.Warn($"[WebSocket Producer] Socket state changed to {client.State}. Reconnecting...");
                        break;
                    }

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        BotLogger.Warn("[WebSocket Producer] Received close frame from Binance. Reconnecting...");
                        try
                        {
                            await client.CloseAsync(WebSocketCloseStatus.NormalClosure, "Reconnecting", token);
                        }
                        catch { /* Ignore close handshake error */ }
                        break;
                    }

                    if (offset > 0)
                    {
                        byte[] channelBuffer = ArrayPool<byte>.Shared.Rent(offset);
                        Array.Copy(receiveBuffer, channelBuffer, offset);

                        if (!_jsonChannel.Writer.TryWrite(new SocketPayload(channelBuffer, offset)))
                        {
                            ArrayPool<byte>.Shared.Return(channelBuffer); // Return if drop occurs
                        }
                    }
                }
                ArrayPool<byte>.Shared.Return(receiveBuffer);
            }
            catch (WebSocketException wsEx)
            {
                reconnectAttempts++;
                BotLogger.Warn($"[WebSocket Producer] Connection exception (Attempt #{reconnectAttempts}): {wsEx.Message}");
            }
            catch (Exception ex)
            {
                reconnectAttempts++;
                BotLogger.Error($"[WebSocket Producer] Unexpected error (Attempt #{reconnectAttempts}): {ex.Message}", ex);
            }


            if (!token.IsCancellationRequested)
            {
                int delayMs = Math.Min(10000, 2000 + (reconnectAttempts * 1000));
                BotLogger.Info($"[WebSocket Producer] Waiting {delayMs}ms before instantiating new ClientWebSocket...");
                await Task.Delay(delayMs, token);
            }
        }
    }

    private static async Task BackgroundConsumerLoopAsync(string interval, CancellationToken token)
    {
        BotLogger.Info("[WebSocket Consumer] Started background zero-allocation processing loop.");

        try
        {
            await foreach (var payload in _jsonChannel.Reader.ReadAllAsync(token))
            {
                try
                {
                    // Zero allocation raw byte parse
                    ProcessKlineMessage(payload.Buffer.AsSpan(0, payload.Length), interval);
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(payload.Buffer);
                }
            }
        }
        catch (OperationCanceledException)
        {
            BotLogger.Info("[WebSocket Consumer] Channel reader loop cancelled.");
        }
        catch (Exception ex)
        {
            BotLogger.Error("[WebSocket Consumer] Error processing frame in consumer loop", ex);
        }
        finally
        {
            while (_jsonChannel.Reader.TryRead(out var leftover))
            {
                ArrayPool<byte>.Shared.Return(leftover.Buffer);
            }
        }
    }

    private static CandleSeriesBuffer CreateBuffer(string key) => new CandleSeriesBuffer(100);

    private static void ProcessKlineMessage(ReadOnlySpan<byte> jsonData, string interval)
    {
        try
        {
            var reader = new System.Text.Json.Utf8JsonReader(jsonData);
            string? stream = null;
            string? symbol = null;
            double closePrice = 0;
            double volume = 0;
            long startTime = 0;
            
            while (reader.Read())
            {
                if (reader.TokenType == System.Text.Json.JsonTokenType.PropertyName)
                {
                    if (reader.ValueTextEquals("stream"u8))
                    {
                        reader.Read();
                        stream = reader.GetString();
                    }
                    else if (reader.ValueTextEquals("s"u8))
                    {
                        reader.Read();
                        symbol = reader.GetString();
                    }
                    else if (reader.ValueTextEquals("c"u8))
                    {
                        reader.Read();
                        if (reader.TokenType == System.Text.Json.JsonTokenType.String)
                            System.Buffers.Text.Utf8Parser.TryParse(reader.ValueSpan, out closePrice, out _);
                        else
                            closePrice = reader.GetDouble();
                    }
                    else if (reader.ValueTextEquals("v"u8))
                    {
                        reader.Read();
                        if (reader.TokenType == System.Text.Json.JsonTokenType.String)
                            System.Buffers.Text.Utf8Parser.TryParse(reader.ValueSpan, out volume, out _);
                        else
                            volume = reader.GetDouble();
                    }
                    else if (reader.ValueTextEquals("t"u8))
                    {
                        reader.Read();
                        startTime = reader.GetInt64();
                    }
                }
            }

            if (stream != null && symbol != null)
            {
                if (stream.Contains("kline") || (closePrice > 0 && volume > 0))
                {
                    if (!string.IsNullOrEmpty(symbol) && closePrice > 0)
                    {
                        string key = $"{ValutaBot.MiniApp.AssetSanitizer.Sanitize(symbol)}_{interval.ToLower()}";

                        var buffer = _liveCandles.GetOrAdd(key, CreateBuffer);
                        buffer.Update(closePrice, volume, startTime);

                        // Migrated from MarketDataService
                        var cleanSymbol = ValutaBot.MiniApp.AssetSanitizer.Sanitize(symbol);
                        var displayKey = symbol.ToUpper().Replace("USDT", "/USDT");
                        LatestPrices[displayKey] = (closePrice, volume, DateTime.UtcNow);
                        CheckVolumeAnomaly(cleanSymbol, closePrice, volume);
                        SignalTracker.UpdatePrice(cleanSymbol, closePrice);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            BotLogger.Warn("[WebSocket Consumer] Error parsing zero-allocation JSON frame", ex);
        }
    }

    private static void CheckVolumeAnomaly(string symbol, double price, double volume24h)
    {
        if (symbol == "BTCUSDT" || symbol == "ETHUSDT" || symbol == "SOLUSDT")
        {
            var buffer = _volumeHistory.GetOrAdd(symbol, _ => new TickRingBuffer(20));
            var snap = buffer.GetOrderedSnapshotRented(20);
            
            try
            {
                buffer.AddTick(volume24h);
                
                if (snap.count >= 20)
                {
                    double avgVol = 0;
                    for (int i = 0; i < snap.count; i++) avgVol += snap.prices[i];
                    avgVol /= snap.count;
                    
                    if (avgVol > 0)
                    {
                        double ratio = volume24h / avgVol;
                        if (ratio > 1.05) 
                        {
                            string alertKey = $"{symbol}_alert_cooldown";
                            if (!LatestPrices.ContainsKey(alertKey) || (DateTime.UtcNow - LatestPrices[alertKey].time).TotalMinutes > 5)
                            {
                                string alertMsg = $"\u26A0\uFE0F {symbol.Replace("USDT", "/USDT")} | Резкий всплеск объёма: +{(ratio - 1) * 100:F1}%";
                                Console.WriteLine($"[MDS] {alertMsg}");

                                RecentAlerts.Enqueue($"{DateTime.UtcNow:HH:mm:ss} {alertMsg}");
                                if (RecentAlerts.Count > 20) RecentAlerts.TryDequeue(out _);
                                
                                LatestPrices[alertKey] = (0, 0, DateTime.UtcNow);
                            }
                        }
                    }
                }
            }
            finally
            {
                System.Buffers.ArrayPool<double>.Shared.Return(snap.prices);
            }
        }
    }
}


