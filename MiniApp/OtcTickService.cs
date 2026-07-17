using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace ValutaBot.MiniApp;

public static class OtcTickService
{
    public class OtcTick
    {
        public double Price { get; set; }
        public DateTime Time { get; set; }
    }

    private static readonly ConcurrentDictionary<string, List<OtcTick>> _ticks = new();
    private static readonly ConcurrentDictionary<string, double> _lastPrices = new();
    private static readonly ConcurrentDictionary<string, DateTime> _lastActivity = new();

    public static void AddTick(string rawAsset, double price)
    {
        if (string.IsNullOrWhiteSpace(rawAsset) || price <= 0) return;

        string asset = NormalizeAsset(rawAsset);
        var tickList = _ticks.GetOrAdd(asset, _ => new List<OtcTick>());

        lock (tickList)
        {
            tickList.Add(new OtcTick { Price = price, Time = DateTime.UtcNow });
            
            // Keep last 5000 ticks (~1.5 hours of high frequency updates)
            if (tickList.Count > 5000)
            {
                tickList.RemoveRange(0, tickList.Count - 5000);
            }
        }

        _lastPrices[asset] = price;
        _lastActivity[asset] = DateTime.UtcNow;

        // Propagate to frontends via WebSocket if registered
        string twelveSymbol = TwelveDataService.ConvertToTwelveSymbol(asset) ?? asset;
        TwelveDataWebSocketManager.UpdatePrice(twelveSymbol, price);
    }

    public static double GetLastPrice(string asset)
    {
        string normalized = NormalizeAsset(asset);
        return _lastPrices.TryGetValue(normalized, out double p) ? p : 0;
    }

    public static bool IsAssetActive(string asset)
    {
        string normalized = NormalizeAsset(asset);
        if (_lastActivity.TryGetValue(normalized, out var lastTime))
        {
            return (DateTime.UtcNow - lastTime).TotalSeconds < 10;
        }
        return false;
    }

    public static string NormalizeAsset(string raw)
    {
        string clean = raw.ToUpper().Replace("_", " ").Replace("-", " ").Trim();
        if (clean.Contains("OTC") && !clean.EndsWith(" OTC"))
        {
            clean = clean.Replace("OTC", "").Trim() + " OTC";
        }
        
        // Handle currency pairs without slash: e.g. "EURUSD OTC" -> "EUR/USD OTC"
        if (clean.Length >= 6)
        {
            string first3 = clean.Substring(0, 3);
            string next3 = clean.Substring(3, 3);
            string remaining = clean.Substring(6);
            if (System.Text.RegularExpressions.Regex.IsMatch(first3, "^[A-Z]{3}$") && 
                System.Text.RegularExpressions.Regex.IsMatch(next3, "^[A-Z]{3}$"))
            {
                clean = $"{first3}/{next3}{remaining}";
            }
        }
        return clean;
    }

    private static int TimeframeSeconds(string tf) => tf.ToLower() switch
    {
        "s3" => 3, "s5" => 5, "s10" => 10, "s15" => 15, "s30" => 30,
        "m1" or "1m" => 60, "m2" or "2m" => 120, "m3" or "3m" => 180, "m5" or "5m" => 300,
        "m15" or "15m" => 900, "m30" or "30m" => 1800,
        "h1" or "1h" => 3600, "h4" or "4h" => 14400,
        "d1" or "1day" or "1d" => 86400, _ => 60
    };

    public static (double[] prices, double[] volumes) GetCandles(string asset, string timeframe, int limit)
    {
        string normalized = NormalizeAsset(asset);
        int tfSec = TimeframeSeconds(timeframe);
        
        var tickList = _ticks.TryGetValue(normalized, out var list) ? list : null;
        List<OtcTick> localTicks = new();
        if (tickList != null)
        {
            lock (tickList)
            {
                localTicks = new List<OtcTick>(tickList);
            }
        }

        List<MiniAppController.OhlcCandle> aggregated = new();

        if (localTicks.Count > 0)
        {
            // Time-bucket the ticks
            long ticksPerBucket = (long)tfSec * TimeSpan.TicksPerSecond;
            
            var groups = localTicks
                .GroupBy(t => t.Time.Ticks / ticksPerBucket)
                .OrderBy(g => g.Key)
                .ToList();

            double lastKnownClose = localTicks[0].Price;

            if (groups.Count > 0)
            {
                long firstBucket = groups[0].Key;
                long lastBucket = groups[^1].Key;

                // Fill sequence of buckets continuously (handling empty time periods)
                for (long b = firstBucket; b <= lastBucket; b++)
                {
                    var g = groups.FirstOrDefault(group => group.Key == b);
                    if (g != null && g.Any())
                    {
                        var sorted = g.OrderBy(t => t.Time).ToList();
                        double open = sorted[0].Price;
                        double close = sorted[^1].Price;
                        double high = sorted.Max(t => t.Price);
                        double low = sorted.Min(t => t.Price);
                        double vol = sorted.Count; // tick count as volume proxy

                        aggregated.Add(new MiniAppController.OhlcCandle(open, high, low, close, vol));
                        lastKnownClose = close;
                    }
                    else
                    {
                        // Forward fill
                        aggregated.Add(new MiniAppController.OhlcCandle(lastKnownClose, lastKnownClose, lastKnownClose, lastKnownClose, 0));
                    }
                }
            }
        }

        // Calibrate volatility for padding based on asset name
        double startPrice = GetLastPrice(normalized);
        if (startPrice <= 0)
        {
            startPrice = normalized.Contains("BTC") ? 64000 
                         : normalized.Contains("ETH") ? 3500 
                         : normalized.Contains("GOLD") ? 2300 
                         : normalized.Contains("JPY") ? 150 
                         : 1.1000;
        }

        double volatility = normalized.Contains("BTC") ? 25.0
                            : normalized.Contains("ETH") ? 3.0
                            : normalized.Contains("GOLD") ? 1.0
                            : normalized.Contains("JPY") ? 0.05
                            : 0.00010; // Forex volatility scale

        // Scale volatility based on timeframe (longer timeframe = larger candle range)
        volatility = volatility * Math.Sqrt(tfSec / 5.0);

        // Pad if we don't have enough candles yet
        if (aggregated.Count < limit)
        {
            int padCount = limit - aggregated.Count;
            var padded = new List<MiniAppController.OhlcCandle>();
            
            double currentPrice = startPrice;
            if (aggregated.Count > 0)
            {
                currentPrice = aggregated[0].Open;
            }

            // Generate walking history backwards
            for (int i = 0; i < padCount; i++)
            {
                double change = (Random.Shared.NextDouble() - 0.5) * volatility;
                double open = currentPrice - change;
                double close = currentPrice;
                double high = Math.Max(open, close) + Random.Shared.NextDouble() * (volatility / 3);
                double low = Math.Min(open, close) - Random.Shared.NextDouble() * (volatility / 3);
                double vol = Random.Shared.Next(5, 20);

                padded.Insert(0, new MiniAppController.OhlcCandle(open, high, low, close, vol));
                currentPrice = open;
            }

            padded.AddRange(aggregated);
            aggregated = padded;
        }

        // Prune to exact limit
        if (aggregated.Count > limit)
        {
            aggregated = aggregated.Skip(aggregated.Count - limit).ToList();
        }

        var ohlcArray = aggregated.ToArray();

        // Push to MiniAppController's candle cache
        string cacheKey = $"{normalized}_{IntervalMap(timeframe)}";
        MiniAppController.SetOhlcCandles(cacheKey, ohlcArray);

        // Separate close prices and volumes
        var prices = ohlcArray.Select(c => c.Close).ToArray();
        var volumes = ohlcArray.Select(c => c.Volume).ToArray();

        return (prices, volumes);
    }

    private static string IntervalMap(string tf) => tf.ToLower() switch
    {
        "s3" or "s5" or "s10" or "s15" or "s30" => "1m",
        "m1" or "1m" => "1m", "m2" or "2m" => "1m", "m3" or "3m" => "3m",
        "m5" or "5m" => "5m", "m15" or "15m" => "15m", "m30" or "30m" => "30m",
        "h1" or "1h" => "1h", "h4" or "4h" => "4h",
        "d1" or "1d" => "1d", _ => "1m"
    };
}
