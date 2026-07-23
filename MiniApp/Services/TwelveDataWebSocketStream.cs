using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace ValutaBot.MiniApp;

public sealed class TwelveDataWebSocketStream : BackgroundService
{
    private static readonly ConcurrentDictionary<string, ConcurrentQueue<double>> _realtimeTicks = new();
    private static readonly ConcurrentDictionary<string, DateTime> _lastTickTime = new();

    /// <summary>
    /// Returns zero-latency in-memory tick prices from RAM (0.001s response time).
    /// Returns null if stream data for asset is not present or stale.
    /// </summary>
    public static double[]? GetRealtimePrices(string asset, int count = 30)
    {
        string cleanKey = NormalizeKey(asset);
        if (_realtimeTicks.TryGetValue(cleanKey, out var q) && q.Count >= 10)
        {
            if (_lastTickTime.TryGetValue(cleanKey, out var lastTime) && (DateTime.UtcNow - lastTime).TotalSeconds < 30)
            {
                var arr = q.ToArray();
                return arr.TakeLast(Math.Min(count, arr.Length)).ToArray();
            }
        }
        return null;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        Console.WriteLine("[TwelveData WS] Starting Persistent Zero-Latency Forex Streaming Service...");

        var symbols = new[]
        {
            "EUR/USD", "GBP/USD", "AUD/USD", "USD/JPY", "USD/CAD", "USD/CHF",
            "NZD/USD", "EUR/GBP", "EUR/JPY", "GBP/JPY", "AUD/JPY", "CAD/JPY"
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
                await ws.ConnectAsync(new Uri(url), ct);
                Console.WriteLine("[TwelveData WS] Persistent Zero-Latency Stream Connected!");

                var subMsg = JsonSerializer.Serialize(new
                {
                    action = "subscribe",
                    @params = new
                    {
                        symbols = string.Join(",", symbols)
                    }
                });

                await ws.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(subMsg)), WebSocketMessageType.Text, true, ct);

                var buffer = new byte[8192];

                while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
                {
                    var result = await ws.ReceiveAsync(buffer, ct);
                    if (result.MessageType != WebSocketMessageType.Text) continue;

                    var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    ParseTick(json);
                }
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                Console.WriteLine($"[TwelveData WS] Exception: {ex.Message}. Reconnecting in 5s...");
                await Task.Delay(5000, ct);
            }
        }
    }

    private static void ParseTick(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("event", out var evProp) && evProp.GetString() == "price")
            {
                if (root.TryGetProperty("symbol", out var symProp) && root.TryGetProperty("price", out var priceProp))
                {
                    string symbol = symProp.GetString() ?? "";
                    double price = 0;

                    if (priceProp.ValueKind == JsonValueKind.Number)
                        price = priceProp.GetDouble();
                    else if (priceProp.ValueKind == JsonValueKind.String)
                        double.TryParse(priceProp.GetString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out price);

                    if (price > 0 && !string.IsNullOrEmpty(symbol))
                    {
                        string cleanKey = NormalizeKey(symbol);
                        var q = _realtimeTicks.GetOrAdd(cleanKey, _ => new ConcurrentQueue<double>());
                        q.Enqueue(price);
                        _lastTickTime[cleanKey] = DateTime.UtcNow;

                        while (q.Count > 100) q.TryDequeue(out _);
                    }
                }
            }
        }
        catch { /* skip malformed ticks */ }
    }

    private static string NormalizeKey(string asset)
    {
        return asset.ToUpper()
                    .Replace("OTC", "")
                    .Replace("_OTC", "")
                    .Replace(" ", "")
                    .Replace("-", "")
                    .Trim();
    }
}
