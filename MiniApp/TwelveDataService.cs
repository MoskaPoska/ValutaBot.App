using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace ValutaBot.MiniApp;

public static class TwelveDataService
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private static readonly ConcurrentDictionary<string, (double[] prices, double[] volumes, DateTime fetchedAt)> _cache = new();
    private static string? _apiKey;

    private static readonly ConcurrentQueue<DateTime> _apiCallTimestamps = new();
    private static readonly object _rateLimitLock = new();

    private static bool CheckAndRegisterRateLimit()
    {
        lock (_rateLimitLock)
        {
            DateTime now = DateTime.UtcNow;
            while (_apiCallTimestamps.TryPeek(out var oldest) && (now - oldest).TotalSeconds > 60)
            {
                _apiCallTimestamps.TryDequeue(out _);
            }

            // TwelveData rate limit is 8 requests per minute. We limit to 7 for safety.
            if (_apiCallTimestamps.Count >= 7)
            {
                return false;
            }

            _apiCallTimestamps.Enqueue(now);
            return true;
        }
    }

    private static string GetApiKey()
    {
        _apiKey ??= Environment.GetEnvironmentVariable("TwelveDataApiKey") ?? "";
        return _apiKey;
    }

    public static async Task<(double[] prices, double[] volumes)?> FetchCandlesAsync(string rawAsset, string interval, int limit = 100, int cacheTtlSeconds = 10)
    {
        string key = $"{rawAsset}_{interval}";

        // 1. Check cache first for fresh data (less than cacheTtlSeconds old)
        if (cacheTtlSeconds > 0 && _cache.TryGetValue(key, out var cached) && (DateTime.UtcNow - cached.fetchedAt).TotalSeconds < cacheTtlSeconds)
        {
            Console.WriteLine($"[TwelveData] Using cached data for {rawAsset} ({interval}) - fresh");
            return (cached.prices, cached.volumes);
        }

        string apiKey = GetApiKey();
        if (string.IsNullOrEmpty(apiKey)) return null;

        // 2. Check rolling rate limiter before making the HTTP API call
        if (!CheckAndRegisterRateLimit())
        {
            if (_cache.TryGetValue(key, out var last))
            {
                Console.WriteLine($"[TwelveData] Rate limit safety triggered (7/8 reached). Serving cache for {rawAsset} ({interval}) immediately to prevent 429.");
                return (last.prices, last.volumes);
            }
            Console.WriteLine($"[TwelveData] Rate limit safety triggered (7/8 reached), but no cache exists for {rawAsset} ({interval})!");
            return null;
        }


        try
        {
            string symbol = ConvertToTwelveSymbol(rawAsset) ?? "";
            string tdInterval = ConvertInterval(interval) ?? "";
            if (string.IsNullOrEmpty(symbol) || string.IsNullOrEmpty(tdInterval)) return null;

            string url = $"https://api.twelvedata.com/time_series?symbol={Uri.EscapeDataString(symbol)}&interval={tdInterval}&outputsize={limit}&apikey={apiKey}";
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd("ValutaBot/1.0");

            var response = await _http.SendAsync(request);
            string body = await response.Content.ReadAsStringAsync();

            using var doc = JsonDocument.Parse(body);

            if (doc.RootElement.TryGetProperty("status", out var status) && status.GetString() == "error")
            {
                var msg = doc.RootElement.TryGetProperty("message", out var m) ? m.GetString() : "";
                BotLogger.Warn($"[TwelveData] API error for {rawAsset}: {msg}");
                throw new Exception($"TwelveData API error: {msg}");
            }

            if (!doc.RootElement.TryGetProperty("values", out var values))
            {
                if (_cache.TryGetValue(key, out var last))
                {
                    BotLogger.Warn($"[TwelveData] No values in response, serving cache for {rawAsset}");
                    return (last.prices, last.volumes);
                }
                return null;
            }

            var arr = values.EnumerateArray().ToList();
            if (arr.Count < 10)
            {
                if (_cache.TryGetValue(key, out var last))
                {
                    BotLogger.Warn($"[TwelveData] Too few candles ({arr.Count}), serving cache for {rawAsset}");
                    return (last.prices, last.volumes);
                }
                return null;
            }

            var prices = arr
                .Select(v => double.Parse(v.GetProperty("close").GetString()!, System.Globalization.CultureInfo.InvariantCulture))
                .Reverse()
                .ToArray();

            var volumes = arr
                .Select(v => v.TryGetProperty("volume", out var volProp) && double.TryParse(volProp.GetString(), System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var vol) ? vol : 0.0)
                .Reverse()
                .ToArray();

            try
            {
                var ohlc = arr.Select(v => new MiniAppController.OhlcCandle(
                    double.Parse(v.GetProperty("open").GetString()!, System.Globalization.CultureInfo.InvariantCulture),
                    v.TryGetProperty("high", out var h) ? double.Parse(h.GetString()!, System.Globalization.CultureInfo.InvariantCulture) : 0,
                    v.TryGetProperty("low", out var l) ? double.Parse(l.GetString()!, System.Globalization.CultureInfo.InvariantCulture) : 0,
                    double.Parse(v.GetProperty("close").GetString()!, System.Globalization.CultureInfo.InvariantCulture),
                    v.TryGetProperty("volume", out var vl) && double.TryParse(vl.GetString(), System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out var volVal) ? volVal : 0
                )).Reverse().ToArray();
                MiniAppController.SetOhlcCandles($"{rawAsset}_{interval}", ohlc);
            }
            catch (Exception ohlcEx)
            {
                BotLogger.Warn($"[TwelveData] OHLC cache parse warning for {rawAsset}", ohlcEx);
            }

            _cache[key] = (prices, volumes, DateTime.UtcNow);
            BotLogger.Info($"[TwelveData] Successfully fetched {prices.Length} candles for {symbol} ({interval})");
            return (prices, volumes);
        }
        catch (JsonException jsonEx)
        {
            BotLogger.Warn($"[TwelveData] JSON parse error for {rawAsset}", jsonEx);

            if (_cache.TryGetValue(key, out var last))
            {
                BotLogger.Info($"[TwelveData] Serving cached fallback data for {rawAsset}");
                return (last.prices, last.volumes);
            }
            return null;
        }
        catch (Exception ex)
        {
            BotLogger.Warn($"[TwelveData] Fetch failed for {rawAsset}: {ex.Message}");

            if (_cache.TryGetValue(key, out var last))
            {
                BotLogger.Info($"[TwelveData] Serving cached fallback data for {rawAsset}");
                return (last.prices, last.volumes);
            }
            return null;
        }
    }

    public static string? ConvertToTwelveSymbol(string raw)
    {
        if (raw.Contains("BTC") || raw.Contains("ETH") || raw.Contains("SOL"))
            return null;

        string a = raw.ToUpper()
            .Replace("OTC", "")
            .Replace("ОТС", "") // Cyrillic
            .Replace(" ", "")
            .Replace("/", "")
            .Replace("-", "")
            .Replace("_", "")
            .Trim();
        if (a == "GOLD" || a == "XAUUSD") return "XAU/USD";
        if (a == "SILVER" || a == "XAGUSD") return "XAG/USD";

        if (a.Contains("/"))
        {
            var parts = a.Split('/');
            if (parts.Length == 2)
            {
                string left = parts[0].Trim();
                string right = parts[1].Trim();

                if (left == "USD") return $"{right}/{left}";

                return a;
            }
        }

        if (a.Length == 6 || a.Length == 7)
        {
            int split = a.Length / 2;
            string left = a[..split];
            string right = a[split..];
            return $"{left}/{right}";
        }

        return null;
    }

    private static string? ConvertInterval(string interval) => interval.ToLower() switch
    {
        "1m" => "1min",
        "2m" => "2min",
        "3m" => "5min",
        "5m" => "5min",
        "15m" => "15min",
        "30m" => "30min",
        "45m" => "45min",
        "1h" or "h1" => "1h",
        "2h" or "h2" => "2h",
        "4h" or "h4" => "4h",
        "1d" or "d1" => "1day",
        _ => null
    };
}

public static class TwelveDataWebSocketManager
{
    private static readonly ConcurrentDictionary<string, ConcurrentDictionary<string, System.Net.WebSockets.WebSocket>> _clients = new();
    private static readonly ConcurrentDictionary<string, (double price, DateTime updatedAt)> _lastPrices = new();
    private static System.Net.WebSockets.ClientWebSocket? _twelveWs;
    private static readonly SemaphoreSlim _lock = new(1, 1);
    private static CancellationTokenSource? _cts;

    private static readonly ConcurrentDictionary<string, ConcurrentQueue<(double price, DateTime timestamp)>> _tickHistory = new();

    public static void RecordTick(string symbol, double price)
    {
        if (string.IsNullOrEmpty(symbol) || price <= 0) return;
        var queue = _tickHistory.GetOrAdd(symbol, _ => new ConcurrentQueue<(double price, DateTime timestamp)>());
        queue.Enqueue((price, DateTime.UtcNow));
        
        // Keep history limited to 15 minutes to save memory
        DateTime cutoff = DateTime.UtcNow.AddMinutes(-15);
        while (queue.TryPeek(out var oldest) && oldest.timestamp < cutoff)
        {
            queue.TryDequeue(out _);
        }
    }

    public static (double price, DateTime timestamp)[] GetTicks(string symbol)
    {
        if (_tickHistory.TryGetValue(symbol, out var queue))
        {
            return queue.ToArray();
        }
        return Array.Empty<(double price, DateTime timestamp)>();
    }

    public static async Task StartBackgroundStreamingAsync()
    {
        string[] defaultSymbols = new[] { "EUR/USD", "GBP/USD", "AUD/USD", "USD/JPY", "USD/CHF", "USD/CAD", "NZD/USD", "BTC/USD" };
        
        await EnsureTwelveWebSocketConnectedAsync();
        foreach (var symbol in defaultSymbols)
        {
            await SubscribeToSymbolAsync(symbol);
        }
    }

    public static double GetLastPrice(string symbol)
    {
        if (_lastPrices.TryGetValue(symbol, out var val) && (DateTime.UtcNow - val.updatedAt).TotalSeconds < 15)
        {
            return val.price;
        }
        return 0;
    }

    public static void UpdatePrice(string symbol, double price)
    {
        if (string.IsNullOrEmpty(symbol) || price <= 0) return;
        _lastPrices[symbol] = (price, DateTime.UtcNow);
        _ = BroadcastToClientsAsync(symbol, price);
    }

    public static async Task RegisterClientAsync(string asset, string clientId, System.Net.WebSockets.WebSocket clientWs)
    {
        string symbol = TwelveDataService.ConvertToTwelveSymbol(asset) ?? asset;
        
        _clients.GetOrAdd(symbol, _ => new())[clientId] = clientWs;
        Console.WriteLine($"[WS Manager] Client {clientId} subscribed to {symbol}");

        await EnsureTwelveWebSocketConnectedAsync();
        await SubscribeToSymbolAsync(symbol);
        
        if (_lastPrices.TryGetValue(symbol, out var lastVal) && (DateTime.UtcNow - lastVal.updatedAt).TotalSeconds < 15)
        {
            await SendToClientAsync(clientWs, symbol, lastVal.price);
        }
    }

    public static void UnregisterClient(string asset, string clientId)
    {
        string symbol = TwelveDataService.ConvertToTwelveSymbol(asset) ?? asset;
        if (_clients.TryGetValue(symbol, out var dict))
        {
            dict.TryRemove(clientId, out _);
        }
    }

    private static async Task EnsureTwelveWebSocketConnectedAsync()
    {
        if (_twelveWs != null && _twelveWs.State == System.Net.WebSockets.WebSocketState.Open)
            return;

        await _lock.WaitAsync();
        try
        {
            if (_twelveWs != null && _twelveWs.State == System.Net.WebSockets.WebSocketState.Open)
                return;

            string apiKey = Environment.GetEnvironmentVariable("TwelveDataApiKey") ?? "";
            if (string.IsNullOrEmpty(apiKey))
            {
                Console.WriteLine("[WS Manager] TwelveData API Key is missing, cannot start WebSocket connection.");
                return;
            }

            _cts?.Cancel();
            _cts = new CancellationTokenSource();

            _twelveWs = new System.Net.WebSockets.ClientWebSocket();
            string wsUrl = $"wss://ws.twelvedata.com/v1/quotes/price?apikey={apiKey}";
            
            Console.WriteLine("[WS Manager] Connecting to TwelveData WebSocket...");
            await _twelveWs.ConnectAsync(new Uri(wsUrl), _cts.Token);
            Console.WriteLine("[WS Manager] TwelveData WebSocket connected!");

            _ = Task.Run(() => ReceiveLoopAsync(_cts.Token));

            foreach (var symbol in _clients.Keys)
            {
                await SubscribeToSymbolAsync(symbol);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WS Manager] Connection error: {ex.Message}");
        }
        finally
        {
            _lock.Release();
        }
    }

    private static async Task SubscribeToSymbolAsync(string symbol)
    {
        if (_twelveWs == null || _twelveWs.State != System.Net.WebSockets.WebSocketState.Open)
            return;

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
            string json = JsonSerializer.Serialize(subMsg);
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            await _twelveWs.SendAsync(new ArraySegment<byte>(bytes), System.Net.WebSockets.WebSocketMessageType.Text, true, CancellationToken.None);
            Console.WriteLine($"[WS Manager] Subscribed to {symbol} on TwelveData");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WS Manager] Subscription failed for {symbol}: {ex.Message}");
        }
    }

    private static async Task ReceiveLoopAsync(CancellationToken ct)
    {
        var buffer = new byte[8192];
        while (!ct.IsCancellationRequested && _twelveWs != null && _twelveWs.State == System.Net.WebSockets.WebSocketState.Open)
        {
            try
            {
                var result = await _twelveWs.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                if (result.MessageType == System.Net.WebSockets.WebSocketMessageType.Close)
                {
                    Console.WriteLine("[WS Manager] TwelveData closed the connection.");
                    break;
                }

                string msg = Encoding.UTF8.GetString(buffer, 0, result.Count);
                while (!result.EndOfMessage)
                {
                    var extraBuffer = new byte[4096];
                    result = await _twelveWs.ReceiveAsync(new ArraySegment<byte>(extraBuffer), ct);
                    msg += Encoding.UTF8.GetString(extraBuffer, 0, result.Count);
                }

                using var doc = JsonDocument.Parse(msg);
                var root = doc.RootElement;
                if (root.TryGetProperty("event", out var ev) && ev.GetString() == "price")
                {
                    string? symbol = root.TryGetProperty("symbol", out var symProp) ? symProp.GetString() : null;
                    if (symbol != null && root.TryGetProperty("price", out var priceProp))
                    {
                        double price = 0;
                        if (priceProp.ValueKind == JsonValueKind.Number)
                        {
                            price = priceProp.GetDouble();
                        }
                        else if (priceProp.ValueKind == JsonValueKind.String)
                        {
                            double.TryParse(priceProp.GetString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out price);
                        }

                        if (price > 0)
                        {
                            _lastPrices[symbol] = (price, DateTime.UtcNow);
                            RecordTick(symbol, price);
                            await BroadcastToClientsAsync(symbol, price);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WS Manager] Error in receive loop: {ex.Message}");
                break;
            }
        }

        if (!ct.IsCancellationRequested)
        {
            Console.WriteLine("[WS Manager] Reconnecting in 5 seconds...");
            try { await Task.Delay(5000, ct); } catch { }
            _ = EnsureTwelveWebSocketConnectedAsync();
        }
    }

    private static async Task BroadcastToClientsAsync(string symbol, double price)
    {
        if (_clients.TryGetValue(symbol, out var dict))
        {
            var deadClients = new List<string>();
            foreach (var pair in dict)
            {
                var clientWs = pair.Value;
                if (clientWs.State == System.Net.WebSockets.WebSocketState.Open)
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
                dict.TryRemove(id, out _);
            }
        }
    }

    private static async Task SendToClientAsync(System.Net.WebSockets.WebSocket ws, string symbol, double price)
    {
        var msgObj = new { symbol, price };
        string json = JsonSerializer.Serialize(msgObj);
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        await ws.SendAsync(new ArraySegment<byte>(bytes), System.Net.WebSockets.WebSocketMessageType.Text, true, CancellationToken.None);
    }
}
