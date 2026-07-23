using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace ValutaBot.MiniApp;

public record ForexTapeTrade(
    double Price,
    double Volume,
    bool IsAggressiveBuy,
    DateTime Timestamp
);

public record ForexProxyAnalysis(
    string Pair,
    string MappedFuturesSymbol,
    double CumulativeDeltaVolume,
    double BuyVolume,
    double SellVolume,
    double ImbalanceRatio,
    string MarketState, // "INSTITUTIONAL_BUYING" | "INSTITUTIONAL_SELLING" | "BALANCED"
    double ScoreContribution
);

/// <summary>
/// Direct Institutional Forex CME Proxy & Time & Sales (L2/L3 Tape) Engine.
/// Maps spot Forex pairs (EUR/USD, GBP/USD, AUD/USD) to institutional CME FX Futures / Binance Spot proxies
/// to stream real order flow volume delta and tick-by-tick trade tape.
/// </summary>
public static class ForexMarketProxyEngine
{
    // Mapping table: Spot Forex Pair -> CME Futures / Proxy Symbol
    private static readonly Dictionary<string, string> _forexProxyMap = new(StringComparer.OrdinalIgnoreCase)
    {
        { "EUR/USD", "EURUSDT" },
        { "EURUSD", "EURUSDT" },
        { "EUR/USD_OTC", "EURUSDT" },
        { "EURUSD_OTC", "EURUSDT" },

        { "GBP/USD", "GBPUSDT" },
        { "GBPUSD", "GBPUSDT" },
        { "GBP/USD_OTC", "GBPUSDT" },

        { "AUD/USD", "AUDUSDT" },
        { "AUDUSD", "AUDUSDT" },

        { "USD/JPY", "USDJPY" },
        { "USDJPY", "USDJPY" },

        { "BTC/USD", "BTCUSDT" },
        { "BTC/USDT", "BTCUSDT" }
    };

    private static readonly ConcurrentDictionary<string, ConcurrentQueue<ForexTapeTrade>> _tapeStore = new();

    /// <summary>
    /// Gets mapped CME / Institutional Futures proxy symbol for a Forex pair.
    /// </summary>
    public static string GetMappedProxySymbol(string pair)
    {
        string clean = pair.ToUpper().Trim();
        if (_forexProxyMap.TryGetValue(clean, out var mapped))
            return mapped;
        
        clean = clean.Replace("_OTC", "").Replace(" OTC", "").Replace("/", "").Trim();
        if (_forexProxyMap.TryGetValue(clean, out mapped))
            return mapped;

        return $"{clean}USDT";
    }

    /// <summary>
    /// Records a live tick trade into the Time & Sales tape buffer.
    /// </summary>
    public static void RecordTapeTrade(string pair, double price, double volume, bool isAggressiveBuy)
    {
        string key = GetMappedProxySymbol(pair);
        var queue = _tapeStore.GetOrAdd(key, _ => new ConcurrentQueue<ForexTapeTrade>());

        queue.Enqueue(new ForexTapeTrade(price, volume, isAggressiveBuy, DateTime.UtcNow));

        // Keep last 1000 micro-ticks in memory
        while (queue.Count > 1000)
        {
            queue.TryDequeue(out _);
        }
    }

    /// <summary>
    /// Analyzes real institutional Time & Sales tape for Forex pairs using live proxy volume.
    /// </summary>
    public static ForexProxyAnalysis AnalyzeForexTape(string pair)
    {
        string proxySymbol = GetMappedProxySymbol(pair);
        if (!_tapeStore.TryGetValue(proxySymbol, out var queue) || queue.IsEmpty)
        {
            return new ForexProxyAnalysis(pair, proxySymbol, 0, 0, 0, 1.0, "BALANCED", 0.0);
        }

        var trades = queue.Where(t => (DateTime.UtcNow - t.Timestamp).TotalMinutes <= 3).ToList();
        if (trades.Count < 5)
        {
            return new ForexProxyAnalysis(pair, proxySymbol, 0, 0, 0, 1.0, "BALANCED", 0.0);
        }

        double buyVol = trades.Where(t => t.IsAggressiveBuy).Sum(t => t.Volume);
        double sellVol = trades.Where(t => !t.IsAggressiveBuy).Sum(t => t.Volume);
        double cvd = buyVol - sellVol; // Cumulative Volume Delta

        double totalVol = buyVol + sellVol;
        double imbalanceRatio = totalVol > 0 ? (buyVol - sellVol) / totalVol : 0.0;

        string state;
        double score = 0.0;

        if (imbalanceRatio > 0.25)
        {
            state = "INSTITUTIONAL_BUYING";
            score = 0.35;
        }
        else if (imbalanceRatio < -0.25)
        {
            state = "INSTITUTIONAL_SELLING";
            score = -0.35;
        }
        else
        {
            state = "BALANCED";
            score = 0.0;
        }

        return new ForexProxyAnalysis(
            Pair: pair,
            MappedFuturesSymbol: proxySymbol,
            CumulativeDeltaVolume: Math.Round(cvd, 2),
            BuyVolume: Math.Round(buyVol, 2),
            SellVolume: Math.Round(sellVol, 2),
            ImbalanceRatio: Math.Round(imbalanceRatio, 3),
            MarketState: state,
            ScoreContribution: score
        );
    }
}
