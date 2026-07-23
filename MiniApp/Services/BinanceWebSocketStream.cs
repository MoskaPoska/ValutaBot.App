using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace ValutaBot.MiniApp;

/// <summary>
/// High-throughput Producer-Consumer WebSocket Client for Binance Tick Streams.
/// Uses System.Threading.Channels to decouple network socket reading (Producer)
/// from JSON parsing and SMC/OrderFlow processing (Consumer).
/// </summary>
public static class BinanceWebSocketStream
{
    private static readonly ConcurrentDictionary<string, (double[] prices, double[] volumes, DateTime updatedAt)> _liveCandles = new();
    
    // High-performance Bounded Channel for Producer-Consumer decoupling
    private static readonly Channel<string> _jsonChannel = Channel.CreateBounded<string>(new BoundedChannelOptions(2000)
    {
        SingleWriter = true,
        SingleReader = true,
        FullMode = BoundedChannelFullMode.DropOldest
    });

    private static CancellationTokenSource? _cts;
    private static bool _isRunning = false;

    public static bool TryGetLiveCandles(string symbol, string interval, out double[] prices, out double[] volumes)
    {
        string key = $"{symbol.ToUpper()}_{interval.ToLower()}";
        if (_liveCandles.TryGetValue(key, out var data) && (DateTime.UtcNow - data.updatedAt).TotalSeconds < 5)
        {
            prices = data.prices;
            volumes = data.volumes;
            return true;
        }

        prices = Array.Empty<double>();
        volumes = Array.Empty<double>();
        return false;
    }

    public record OrderbookDepthSnapshot(
        double TotalBidVolume,
        double TotalAskVolume,
        double ImbalanceRatio, // Range -1.0 to +1.0
        DateTime UpdatedAt
    );

    private static readonly ConcurrentDictionary<string, OrderbookDepthSnapshot> _liveOrderbooks = new();

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
            streams.Add($"{s.ToLower()}@kline_{interval}");
            streams.Add($"{s.ToLower()}@depth20@100ms");
        }
        string streamNames = string.Join("/", streams);
        string wsUrl = $"wss://stream.binance.com:9443/ws/{streamNames}";

        // 1. Launch Consumer Background Loop (Processes Channel Queue)
        _ = Task.Run(() => BackgroundConsumerLoopAsync(interval, _cts.Token));

        // 2. Launch Producer Network Socket Loop (Reads Network & Writes to Channel)
        _ = Task.Run(() => ProducerNetworkLoopAsync(wsUrl, _cts.Token));
    }

    public static void Stop()
    {
        _cts?.Cancel();
        _isRunning = false;
        BotLogger.Info("[WebSocket Producer] WebSocket stream stopped and disconnected.");
    }

    /// <summary>
    /// Producer: Reads raw socket frames from WebSocket network thread and pushes immediately to Channel.
    /// Zero JSON parsing or processing on the network thread.
    /// </summary>
    private static async Task ProducerNetworkLoopAsync(string url, CancellationToken token)
    {
        byte[] buffer = new byte[8192];
        int reconnectAttempts = 0;

        while (!token.IsCancellationRequested)
        {
            ClientWebSocket? client = null;
            try
            {
                client = new ClientWebSocket();
                client.Options.SetRequestHeader("User-Agent", "ValutaBot/1.0");

                BotLogger.Info($"[WebSocket Producer] Connecting to Binance real-time stream: {url}");
                await client.ConnectAsync(new Uri(url), token);
                reconnectAttempts = 0;
                BotLogger.Info("[WebSocket Producer] Connected successfully to Binance WebSocket stream!");

                while (client.State == WebSocketState.Open && !token.IsCancellationRequested)
                {
                    using var ms = new MemoryStream();
                    WebSocketReceiveResult? result = null;

                    try
                    {
                        do
                        {
                            result = await client.ReceiveAsync(new ArraySegment<byte>(buffer), token);
                            ms.Write(buffer, 0, result.Count);
                        }
                        while (!result.EndOfMessage && !token.IsCancellationRequested);
                    }
                    catch (WebSocketException wsEx)
                    {
                        BotLogger.Warn($"[WebSocket Producer] Network frame receive error: {wsEx.Message}. Socket will reconnect.");
                        break;
                    }

                    if (token.IsCancellationRequested || client.State == WebSocketState.Aborted || client.State == WebSocketState.Closed)
                    {
                        BotLogger.Warn($"[WebSocket Producer] Socket state changed to {client.State}. Reconnecting...");
                        break;
                    }

                    if (result != null && result.MessageType == WebSocketMessageType.Close)
                    {
                        BotLogger.Warn("[WebSocket Producer] Received close frame from Binance. Reconnecting...");
                        try
                        {
                            await client.CloseAsync(WebSocketCloseStatus.NormalClosure, "Reconnecting", token);
                        }
                        catch { /* Ignore close handshake error */ }
                        break;
                    }

                    ms.Seek(0, SeekOrigin.Begin);
                    string json = Encoding.UTF8.GetString(ms.ToArray());

                    // Fast non-blocking push to Channel queue
                    _jsonChannel.Writer.TryWrite(json);
                }
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
            finally
            {
                try
                {
                    client?.Dispose();
                }
                catch { /* Ensure clean disposal */ }
            }

            if (!token.IsCancellationRequested)
            {
                int delayMs = Math.Min(10000, 2000 + (reconnectAttempts * 1000));
                BotLogger.Info($"[WebSocket Producer] Waiting {delayMs}ms before instantiating new ClientWebSocket...");
                await Task.Delay(delayMs, token);
            }
        }
    }

    /// <summary>
    /// Consumer: Background worker processing JSON frames from Channel queue.
    /// Updates live price/volume state and feeds SMC & OrderFlow processing.
    /// </summary>
    private static async Task BackgroundConsumerLoopAsync(string interval, CancellationToken token)
    {
        BotLogger.Info("[WebSocket Consumer] Started background processing loop for Channel queue.");

        try
        {
            await foreach (string jsonMessage in _jsonChannel.Reader.ReadAllAsync(token))
            {
                ProcessKlineMessage(jsonMessage, interval);
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
    }

    private static void ProcessKlineMessage(string jsonMessage, string interval)
    {
        try
        {
            using var doc = JsonDocument.Parse(jsonMessage);
            var root = doc.RootElement;

            if (root.TryGetProperty("data", out var dataProp))
            {
                root = dataProp;
            }

            // Real-Time Orderbook Depth Stream (@depth20@100ms)
            if (root.TryGetProperty("bids", out var bidsProp) && root.TryGetProperty("asks", out var asksProp))
            {
                string symbol = root.TryGetProperty("s", out var sProp) ? (sProp.GetString() ?? "") : "";
                double totalBidVol = 0;
                double totalAskVol = 0;

                foreach (var bid in bidsProp.EnumerateArray())
                {
                    if (double.TryParse(bid[1].GetString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double qty))
                        totalBidVol += qty;
                }

                foreach (var ask in asksProp.EnumerateArray())
                {
                    if (double.TryParse(ask[1].GetString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double qty))
                        totalAskVol += qty;
                }

                double sum = totalBidVol + totalAskVol;
                double imbalance = sum > 0 ? (totalBidVol - totalAskVol) / sum : 0.0;

                if (!string.IsNullOrEmpty(symbol))
                {
                    _liveOrderbooks[symbol.ToUpper()] = new OrderbookDepthSnapshot(
                        TotalBidVolume: Math.Round(totalBidVol, 2),
                        TotalAskVolume: Math.Round(totalAskVol, 2),
                        ImbalanceRatio: Math.Round(imbalance, 3),
                        UpdatedAt: DateTime.UtcNow
                    );
                }
                return;
            }

            if (root.TryGetProperty("s", out var symbolProp) && root.TryGetProperty("k", out var klineProp))
            {
                string symbol = symbolProp.GetString() ?? "";
                double closePrice = double.Parse(klineProp.GetProperty("c").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
                double volume = double.Parse(klineProp.GetProperty("v").GetString()!, System.Globalization.CultureInfo.InvariantCulture);

                string key = $"{symbol.ToUpper()}_{interval.ToLower()}";

                _liveCandles.AddOrUpdate(
                    key,
                    (new[] { closePrice }, new[] { volume }, DateTime.UtcNow),
                    (k, existing) =>
                    {
                        var newPrices = existing.prices.Concat(new[] { closePrice }).TakeLast(100).ToArray();
                        var newVolumes = existing.volumes.Concat(new[] { volume }).TakeLast(100).ToArray();
                        return (newPrices, newVolumes, DateTime.UtcNow);
                    }
                );
            }
        }
        catch (Exception ex)
        {
            BotLogger.Warn("[WebSocket Consumer] Error parsing kline JSON frame", ex);
        }
    }

    public static void StopStream()
    {
        _cts?.Cancel();
        _jsonChannel.Writer.TryComplete();
        _isRunning = false;
        BotLogger.Info("[WebSocket] Stream and Channel consumer stopped.");
    }
}
