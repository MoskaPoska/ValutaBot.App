using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using StackExchange.Redis;

namespace ValutaBot.MiniApp.Services;

public class RedisQuote
{
    [JsonPropertyName("symbol")]
    public string Symbol { get; set; } = string.Empty;

    [JsonPropertyName("price")]
    public double Price { get; set; }

    [JsonPropertyName("timestamp")]
    public long Timestamp { get; set; }

    [JsonPropertyName("latency_ms")]
    public double LatencyMs { get; set; }

    [JsonPropertyName("server_time")]
    public long ServerTime { get; set; }
}

public class LiveCandleAggregator : IHostedService
{
    private readonly IConfiguration _config;
    private readonly ILogger<LiveCandleAggregator> _logger;
    private ConnectionMultiplexer? _redis;
    private ISubscriber? _subscriber;

    // symbol -> interval -> List of completed candles
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, List<MiniAppController.OhlcCandle>>> _historicalCandles = new();
    
    // symbol -> interval -> Current open candle
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, MiniAppController.OhlcCandle>> _currentCandles = new();

    public static LiveCandleAggregator? Instance { get; private set; }

    public LiveCandleAggregator(IConfiguration config, ILogger<LiveCandleAggregator> logger)
    {
        _config = config;
        _logger = logger;
        Instance = this;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        string redisUrl = _config["REDIS_URL"] ?? Environment.GetEnvironmentVariable("REDIS_URL") ?? "localhost:6379";
        _logger.LogInformation($"[LiveCandleAggregator] Connecting to Redis at {redisUrl}...");
        
        try
        {
            ConfigurationOptions options;
            if (redisUrl.StartsWith("redis://") || redisUrl.StartsWith("rediss://"))
            {
                var uri = new Uri(redisUrl);
                options = new ConfigurationOptions
                {
                    EndPoints = { { uri.Host, uri.Port > 0 ? uri.Port : 6379 } },
                    AbortOnConnectFail = false
                };
                var userInfo = uri.UserInfo.Split(':');
                if (userInfo.Length == 2)
                {
                    options.User = userInfo[0];
                    options.Password = userInfo[1];
                }
                else if (userInfo.Length == 1 && !string.IsNullOrEmpty(userInfo[0]))
                {
                    options.Password = userInfo[0];
                }
            }
            else
            {
                options = ConfigurationOptions.Parse(redisUrl);
                options.AbortOnConnectFail = false;
            }
            _redis = await ConnectionMultiplexer.ConnectAsync(options);
            _subscriber = _redis.GetSubscriber();

            await _subscriber.SubscribeAsync(new RedisChannel("quotes:*", RedisChannel.PatternMode.Pattern), (channel, message) =>
            {
                if (message.IsNullOrEmpty) return;
                try
                {
                    var quote = JsonSerializer.Deserialize<RedisQuote>((string)message!);
                    if (quote != null)
                    {
                        ProcessQuote(quote);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"[LiveCandleAggregator] Failed to parse quote: {ex.Message}");
                }
            });

            _logger.LogInformation("[LiveCandleAggregator] Subscribed to quotes:*");
        }
        catch (Exception ex)
        {
            _logger.LogError($"[LiveCandleAggregator] Redis connection failed: {ex.Message}");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _redis?.Dispose();
        return Task.CompletedTask;
    }

    private void ProcessQuote(RedisQuote quote)
    {
        // Normalize symbol to uppercase for consistent lookups
        string normalizedSymbol = quote.Symbol.ToUpper();
        
        // For every supported interval, update the candle
        string[] intervals = { "1m", "5m", "15m", "30m", "1h" };
        DateTime quoteTime = DateTimeOffset.FromUnixTimeSeconds(quote.Timestamp).UtcDateTime;

        foreach (var interval in intervals)
        {
            UpdateCandle(normalizedSymbol, interval, quote.Price, quoteTime);
        }
    }

    private void UpdateCandle(string symbol, string interval, double price, DateTime time)
    {
        var symbolHist = _historicalCandles.GetOrAdd(symbol, _ => new ConcurrentDictionary<string, List<MiniAppController.OhlcCandle>>());
        var symbolCurr = _currentCandles.GetOrAdd(symbol, _ => new ConcurrentDictionary<string, MiniAppController.OhlcCandle>());

        var histList = symbolHist.GetOrAdd(interval, _ => new List<MiniAppController.OhlcCandle>());
        
        DateTime candleStart = GetCandleStart(time, interval);

        symbolCurr.AddOrUpdate(interval, 
            // Add new candle
            addValueFactory: (_) => new MiniAppController.OhlcCandle(price, price, price, price, 1, candleStart),
            // Update existing candle
            updateValueFactory: (_, current) => 
            {
                if (current.Timestamp == candleStart)
                {
                    return current with
                    {
                        High = Math.Max(current.High, price),
                        Low = Math.Min(current.Low, price),
                        Close = price,
                        Volume = current.Volume + 1
                    };
                }
                else
                {
                    // Candle closed! Move to history
                    lock (histList)
                    {
                        histList.Add(current);
                        // Keep max 1000 candles in memory
                        if (histList.Count > 1000)
                        {
                            histList.RemoveAt(0);
                        }
                    }
                    // Start new candle
                    return new MiniAppController.OhlcCandle(price, price, price, price, 1, candleStart);
                }
            });
    }

    private DateTime GetCandleStart(DateTime time, string interval)
    {
        int minutes = interval switch
        {
            "1m" => 1,
            "3m" => 3,
            "5m" => 5,
            "15m" => 15,
            "30m" => 30,
            "1h" => 60,
            _ => 1
        };

        // Truncate to the nearest multiple of 'minutes'
        int m = (time.Minute / minutes) * minutes;
        return new DateTime(time.Year, time.Month, time.Day, time.Hour, m, 0, DateTimeKind.Utc);
    }

    public MiniAppController.OhlcCandle[] GetCandles(string symbol, string interval, int limit)
    {
        var result = new List<MiniAppController.OhlcCandle>();

        // Add historical closed candles
        if (_historicalCandles.TryGetValue(symbol, out var symbolHist) &&
            symbolHist.TryGetValue(interval, out var histList))
        {
            lock (histList)
            {
                int skip = Math.Max(0, histList.Count - limit);
                result.AddRange(histList.Skip(skip));
            }
        }

        // Always add the current open candle if it exists (even if history is empty)
        if (_currentCandles.TryGetValue(symbol, out var symbolCurr) &&
            symbolCurr.TryGetValue(interval, out var current))
        {
            result.Add(current);
        }

        // Trim to limit
        if (result.Count > limit)
            result = result.Skip(result.Count - limit).ToList();

        return result.ToArray();
    }
}
