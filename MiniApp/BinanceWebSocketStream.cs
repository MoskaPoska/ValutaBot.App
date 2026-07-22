using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace ValutaBot.MiniApp;

public static class BinanceWebSocketStream
{
    private static readonly ConcurrentDictionary<string, (double[] prices, double[] volumes, DateTime updatedAt)> _liveCandles = new();
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

    public static void StartStream(IEnumerable<string> symbols, string interval = "1m")
    {
        if (_isRunning) return;
        _isRunning = true;
        _cts = new CancellationTokenSource();

        string streamNames = string.Join("/", symbols.Select(s => $"{s.ToLower()}@kline_{interval}"));
        string wsUrl = $"wss://stream.binance.com:9443/ws/{streamNames}";

        _ = Task.Run(() => ConnectAndListenAsync(wsUrl, interval, _cts.Token));
    }

    private static async Task ConnectAndListenAsync(string url, string interval, CancellationToken token)
    {
        byte[] buffer = new byte[8192];
        int reconnectAttempts = 0;

        // ─── Unbreakable Infinite Socket Loop ───
        while (!token.IsCancellationRequested)
        {
            ClientWebSocket? client = null;
            try
            {
                client = new ClientWebSocket();
                client.Options.SetRequestHeader("User-Agent", "ValutaBot/1.0");

                BotLogger.Info($"[WebSocket] Connecting to Binance real-time stream: {url}");
                await client.ConnectAsync(new Uri(url), token);
                reconnectAttempts = 0; // Reset reconnect attempts counter on successful connection
                BotLogger.Info("[WebSocket] Connected successfully to Binance WebSocket stream!");

                // ─── Inner Frame Reading Loop ───
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
                        BotLogger.Warn($"[WebSocket] Network frame receive error: {wsEx.Message}. Socket will reconnect.");
                        break; // Exit inner loop to force socket recreation
                    }

                    if (token.IsCancellationRequested || client.State == WebSocketState.Aborted || client.State == WebSocketState.Closed)
                    {
                        BotLogger.Warn($"[WebSocket] Socket state changed to {client.State}. Reconnecting...");
                        break;
                    }

                    if (result != null && result.MessageType == WebSocketMessageType.Close)
                    {
                        BotLogger.Warn("[WebSocket] Received close frame from Binance. Reconnecting...");
                        try
                        {
                            await client.CloseAsync(WebSocketCloseStatus.NormalClosure, "Reconnecting", token);
                        }
                        catch { /* Ignore close handshake error */ }
                        break;
                    }

                    ms.Seek(0, SeekOrigin.Begin);
                    string json = Encoding.UTF8.GetString(ms.ToArray());
                    ProcessKlineMessage(json, interval);
                }
            }
            catch (WebSocketException wsEx)
            {
                reconnectAttempts++;
                BotLogger.Warn($"[WebSocket] Connection exception (Attempt #{reconnectAttempts}): {wsEx.Message}");
            }
            catch (Exception ex)
            {
                reconnectAttempts++;
                BotLogger.Error($"[WebSocket] Unexpected error (Attempt #{reconnectAttempts}): {ex.Message}", ex);
            }
            finally
            {
                try
                {
                    client?.Dispose();
                }
                catch { /* Ensure clean disposal */ }
            }

            // Exponential backoff before reconnecting (between 2s and 10s)
            if (!token.IsCancellationRequested)
            {
                int delayMs = Math.Min(10000, 2000 + (reconnectAttempts * 1000));
                BotLogger.Info($"[WebSocket] Waiting {delayMs}ms before instantiating new ClientWebSocket...");
                await Task.Delay(delayMs, token);
            }
        }
    }

    private static void ProcessKlineMessage(string jsonMessage, string interval)
    {
        try
        {
            using var doc = JsonDocument.Parse(jsonMessage);
            var root = doc.RootElement;

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
            BotLogger.Warn("[WebSocket] Error processing kline frame", ex);
        }
    }

    public static void StopStream()
    {
        _cts?.Cancel();
        _isRunning = false;
        BotLogger.Info("[WebSocket] Stream stopped.");
    }
}
