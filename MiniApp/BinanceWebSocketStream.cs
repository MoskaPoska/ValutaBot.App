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

        while (!token.IsCancellationRequested)
        {
            using var client = new ClientWebSocket();
            try
            {
                BotLogger.Info($"[WebSocket] Connecting to Binance real-time stream: {url}");
                await client.ConnectAsync(new Uri(url), token);
                BotLogger.Info("[WebSocket] Connected successfully to Binance WebSocket!");

                while (client.State == WebSocketState.Open && !token.IsCancellationRequested)
                {
                    using var ms = new MemoryStream();
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await client.ReceiveAsync(new ArraySegment<byte>(buffer), token);
                        ms.Write(buffer, 0, result.Count);
                    }
                    while (!result.EndOfMessage);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await client.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", token);
                        break;
                    }

                    ms.Seek(0, SeekOrigin.Begin);
                    string json = Encoding.UTF8.GetString(ms.ToArray());
                    ProcessKlineMessage(json, interval);
                }
            }
            catch (Exception ex)
            {
                if (!token.IsCancellationRequested)
                {
                    BotLogger.Warn($"[WebSocket] Connection dropped ({ex.Message}), reconnecting in 3s...");
                    await Task.Delay(3000, token);
                }
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
