using Skender.Stock.Indicators;

namespace ValutaBot.MiniApp;

/// <summary>
/// Technical analysis engine using the industry-standard Skender.Stock.Indicators library.
/// Provides SIMD-optimized, zero-lag adaptive calculations for HMA (Hull Moving Average),
/// KAMA (Kaufman Adaptive Moving Average), Connors RSI, MACD, True ADX, ATR, and Bollinger Bands.
/// </summary>
public class TechnicalAnalysisEngine : ITechnicalAnalysisEngine
{
    public static ITechnicalAnalysisEngine Instance { get; set; } = new TechnicalAnalysisEngine();
    /// <param name="intervalMinutes">Candle interval in minutes (e.g. 1 for m1, 5 for m5, 0.25 for s15). Used to space Quote timestamps correctly so Skender time-dependent calculations work properly.</param>
    private IEnumerable<Quote> ConvertToQuotes(double[] prices, double[]? volumes = null, MiniAppController.OhlcCandle[]? candles = null, double intervalMinutes = 1.0)
    {
        TimeSpan interval = TimeSpan.FromMinutes(intervalMinutes);

        if (candles != null && candles.Length > 0)
        {
            DateTime startTime = DateTime.UtcNow - interval * candles.Length;
            for (int i = 0; i < candles.Length; i++)
            {
                var c = candles[i];
                yield return new Quote
                {
                    Date = startTime + interval * i,
                    Open = (decimal)c.Open,
                    High = (decimal)c.High,
                    Low = (decimal)c.Low,
                    Close = (decimal)c.Close,
                    Volume = (decimal)c.Volume
                };
            }
        }
        else if (prices != null && prices.Length > 0)
        {
            DateTime startTime = DateTime.UtcNow - interval * prices.Length;
            for (int i = 0; i < prices.Length; i++)
            {
                decimal p = (decimal)prices[i];
                decimal v = (volumes != null && i < volumes.Length) ? (decimal)volumes[i] : 1.0m;
                yield return new Quote
                {
                    Date = startTime + interval * i,
                    Open = p,
                    High = p,
                    Low = p,
                    Close = p,
                    Volume = v
                };
            }
        }
    }

    public double ComputeRsi(double[] data, int period = 14)
    {
        var quotes = ConvertToQuotes(data);
        if (quotes.Count() < period + 1) return 50.0;

        var results = quotes.GetRsi(period);
        var last = results.LastOrDefault();
        return last?.Rsi.HasValue == true ? (double)last.Rsi.Value : 50.0;
    }

    public double ComputeConnorsRsi(double[] data)
    {
        var quotes = ConvertToQuotes(data);
        if (quotes.Count() < 20) return ComputeRsi(data, 14);

        try
        {
            var results = quotes.GetConnorsRsi(3, 2, 10);
            var last = results.LastOrDefault();
            return last?.ConnorsRsi.HasValue == true ? (double)last.ConnorsRsi.Value : ComputeRsi(data, 14);
        }
        catch
        {
            return ComputeRsi(data, 14);
        }
    }

    public double ComputeHma(double[] data, int period = 9)
    {
        var quotes = ConvertToQuotes(data);
        if (quotes.Count() < period) return data.Length > 0 ? data[^1] : 0.0;

        try
        {
            var results = quotes.GetHma(period);
            var last = results.LastOrDefault();
            return last?.Hma.HasValue == true ? (double)last.Hma.Value : ComputeEma(data, period);
        }
        catch
        {
            return ComputeEma(data, period);
        }
    }
    
    public (double[] Hma, double[] ConnorsRsi) ComputeWalkForwardBatch(double[] data)
    {
        var quotes = ConvertToQuotes(data);
        var hmaResult = new double[data.Length];
        var rsiResult = new double[data.Length];
        
        if (quotes.Count() < 20)
        {
            // Fallback for very small arrays
            for (int i = 0; i < data.Length; i++)
            {
                hmaResult[i] = data[i];
                rsiResult[i] = 50.0;
            }
            return (hmaResult, rsiResult);
        }

        try
        {
            var hmaList = quotes.GetHma(9).ToList();
            var rsiList = quotes.GetConnorsRsi(3, 2, 10).ToList();
            
            for (int i = 0; i < data.Length; i++)
            {
                var h = hmaList[i].Hma;
                hmaResult[i] = h.HasValue ? (double)h.Value : data[i];
                
                var r = rsiList[i].ConnorsRsi;
                rsiResult[i] = r.HasValue ? (double)r.Value : 50.0;
            }
        }
        catch
        {
            for (int i = 0; i < data.Length; i++)
            {
                hmaResult[i] = data[i];
                rsiResult[i] = 50.0;
            }
        }
        
        return (hmaResult, rsiResult);
    }

    public double ComputeEma(double[] data, int period = 9)
    {
        var quotes = ConvertToQuotes(data);
        if (quotes.Count() < period) return data.Length > 0 ? data[^1] : 0.0;

        var results = quotes.GetEma(period);
        var last = results.LastOrDefault();
        return last?.Ema.HasValue == true ? (double)last.Ema.Value : data[^1];
    }

    /// <summary>Computes MACD line and signal line for the latest candle.</summary>
    public (double macd, double signal) ComputeMacd(double[] data)
    {
        var quotes = ConvertToQuotes(data);
        if (quotes.Count() < 26) return (0.0, 0.0);

        var results = quotes.GetMacd(12, 26, 9);
        var last = results.LastOrDefault();
        double macdLine = last?.Macd.HasValue == true ? (double)last.Macd.Value : 0.0;
        double signalLine = last?.Signal.HasValue == true ? (double)last.Signal.Value : 0.0;
        return (macdLine, signalLine);
    }

    public (double adx, double pdi, double mdi) ComputeTrueAdx(MiniAppController.OhlcCandle[] candles, int period = 14)
    {
        var quotes = ConvertToQuotes(Array.Empty<double>(), candles: candles);
        if (quotes.Count() < period + 1) return (20.0, 0.0, 0.0);

        var results = quotes.GetAdx(period);
        var last = results.LastOrDefault();
        if (last == null) return (20.0, 0.0, 0.0);

        double adx = last.Adx.HasValue ? (double)last.Adx.Value : 20.0;
        double pdi = last.Pdi.HasValue ? (double)last.Pdi.Value : 0.0;
        double mdi = last.Mdi.HasValue ? (double)last.Mdi.Value : 0.0;

        return (adx, pdi, mdi);
    }

    public double ComputeAtr(MiniAppController.OhlcCandle[] candles, int period = 14)
    {
        var quotes = ConvertToQuotes(Array.Empty<double>(), candles: candles);
        if (quotes.Count() < period) return 0;

        var results = quotes.GetAtr(period);
        var last = results.LastOrDefault();
        return last?.Atr.HasValue == true ? (double)last.Atr.Value : 0.0;
    }

    public double ComputeBollingerZscore(double[] prices, int period = 20)
    {
        var quotes = ConvertToQuotes(prices);
        if (quotes.Count() < period) return 0.0;

        var results = quotes.GetBollingerBands(period, 2);
        var last = results.LastOrDefault();
        if (last == null || !last.ZScore.HasValue) return 0.0;

        return (double)last.ZScore.Value;
    }

    public (double score, double confidence, double rsiVal, double emaVal, double volStrengthVal, double atrVal) ScoreTimeframe(
        double[] prices, double[] volumes, MiniAppController.OhlcCandle[]? candles = null,
        double? adxOverride = null, double? atrOverride = null, bool isForex = false)
    {
        if (prices.Length < 14) return (0.0, 50.0, 50.0, 0.0, 0.0, 0.0);

        double rsi = ComputeConnorsRsi(prices);
        double hma = ComputeHma(prices, 9);
        double lastPrice = prices[^1];

        var (adxVal, pdiVal, mdiVal) = adxOverride.HasValue
            ? (adxOverride.Value, 0.0, 0.0)
            : (candles != null ? ComputeTrueAdx(candles) : (20.0, 0.0, 0.0));

        double atrVal = atrOverride.HasValue
            ? atrOverride.Value
            : (candles != null ? ComputeAtr(candles) : 0);

        double score = 0;
        double confidence = 60.0;

        // Proportional Connors RSI scoring (-1.0 to +1.0)
        // Adjusted denominator to 40.0 to require more extreme RSI for high scores
        score += (rsi - 50) / 40.0;

        // HMA (Hull Moving Average zero-lag) scoring
        // Lowered from 0.35 to 0.15 so HMA alone cannot breach probability thresholds
        if (lastPrice > hma) score += 0.15;
        else if (lastPrice < hma) score -= 0.15;

        // ADX scoring
        if (adxVal > 25)
        {
            confidence += Math.Min((adxVal - 25) * 0.8, 20);
            if (pdiVal > mdiVal && pdiVal > 0) score += 0.25;
            else if (mdiVal > pdiVal && mdiVal > 0) score -= 0.25;
        }

        // Volume strength scoring вЂ” directional volume ratio contributes to score
        double volStrength = 0.0;
        if (volumes.Length >= 5)
        {
            double avgVol = volumes.Take(volumes.Length - 1).TakeLast(20).Average();
            double lastVol = volumes[^1];
            if (avgVol > 0)
            {
                double ratio = lastVol / avgVol;
                double priceChange = prices.Length >= 2 ? prices[^1] - prices[^2] : 0;
                // Volume surge (ratio > 1.5) in a price direction adds confirmation weight
                // Clamped to В±0.20 so volume alone cannot force a signal
                volStrength = (priceChange >= 0 ? 1 : -1) * Math.Max(0.0, Math.Min(ratio - 1.0, 1.0));
                score += Math.Clamp(volStrength * 0.15, -0.20, 0.20);
            }
        }

        return (score, Math.Clamp(confidence, 50, 95), Math.Round(rsi, 1), Math.Round(hma, 5), Math.Round(volStrength, 2), Math.Round(atrVal, 6));
    }

    public record GatekeeperResult(bool IsTradeable, string Reason, double Atr, double Adx);

    public GatekeeperResult ValidateMarketGatekeeper(double[] prices, MiniAppController.OhlcCandle[]? candles = null)
    {
        if (prices == null || prices.Length < 15)
        {
            return new GatekeeperResult(false, "РќРµРґРѕСЃС‚Р°С‚РѕС‡РЅРѕ РґР°РЅРЅС‹С… С†РµРЅС‹ РґР»СЏ РїСЂРѕРІРµСЂРєРё Gatekeeper", 0, 0);
        }

        if (candles == null || candles.Length < 15)
        {
            BotLogger.Warn("[Gatekeeper] Rejecting trade: Insufficient or missing OHLC candles. Synthetic data is prohibited.");
            return new GatekeeperResult(false, "вљ пёЏ Р”Р°РЅРЅС‹Рµ РѕС‚ Р±РёСЂР¶Рё РЅРµРїРѕР»РЅС‹Рµ. РЎРґРµР»РєР° РѕС‚РєР»РѕРЅРµРЅР° РІ С†РµР»СЏС… Р±РµР·РѕРїР°СЃРЅРѕСЃС‚Рё.", 0, 0);
        }
        double atr = candles != null ? ComputeAtr(candles) : 0;
        var (adx, _, _) = candles != null ? ComputeTrueAdx(candles) : (20.0, 0, 0);

        // Check flat / dead market: if prices didn't move
        double minPrice = prices[^15..].Min();
        double maxPrice = prices[^15..].Max();
        double priceRange = maxPrice - minPrice;

        if (priceRange < 1e-7)
        {
            BotLogger.Warn("[Gatekeeper] Market is completely flat / frozen. Aborting analysis early in 0ms.");
            return new GatekeeperResult(false, "вљ пёЏ Р С‹РЅРѕРє РІ СЃРѕСЃС‚РѕСЏРЅРёРё Р·Р°СЃС‚РѕСЏ (РЅРµС‚ РєРѕР»РµР±Р°РЅРёР№ С†РµРЅС‹).", atr, adx);
        }

        return new GatekeeperResult(true, "Р С‹РЅРѕРє Р°РєС‚РёРІРµРЅ", atr, adx);
    }

    public double CalculateVolatilityRatio(double[] prices)
    {
        if (prices == null || prices.Length < 26) return 1.0;

        double[] returns = new double[25];
        for (int i = 0; i < 25; i++)
        {
            int idx = prices.Length - 25 + i;
            returns[i] = Math.Log(prices[idx] / (prices[idx - 1] + 1e-10));
        }

        var shortReturns = returns.TakeLast(5);
        var longReturns = returns.Take(20);

        double shortVol = MathNet.Numerics.Statistics.Statistics.StandardDeviation(shortReturns);
        double longVol = MathNet.Numerics.Statistics.Statistics.StandardDeviation(longReturns);

        if (longVol < 1e-9) return 1.0;
        return shortVol / longVol;
    }
}

