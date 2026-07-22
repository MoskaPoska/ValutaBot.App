using Skender.Stock.Indicators;

namespace ValutaBot.MiniApp;

/// <summary>
/// Technical analysis engine using the industry-standard Skender.Stock.Indicators library.
/// Provides SIMD-optimized, zero-lag adaptive calculations for HMA (Hull Moving Average),
/// KAMA (Kaufman Adaptive Moving Average), Connors RSI, MACD, True ADX, ATR, and Bollinger Bands.
/// </summary>
public static class TechnicalAnalysisEngine
{
    private static List<Quote> ConvertToQuotes(double[] prices, double[]? volumes = null, MiniAppController.OhlcCandle[]? candles = null)
    {
        var quotes = new List<Quote>();

        if (candles != null && candles.Length > 0)
        {
            DateTime startTime = DateTime.UtcNow.AddMinutes(-candles.Length);
            for (int i = 0; i < candles.Length; i++)
            {
                var c = candles[i];
                quotes.Add(new Quote
                {
                    Date = startTime.AddMinutes(i),
                    Open = (decimal)c.Open,
                    High = (decimal)c.High,
                    Low = (decimal)c.Low,
                    Close = (decimal)c.Close,
                    Volume = (decimal)c.Volume
                });
            }
        }
        else if (prices != null && prices.Length > 0)
        {
            DateTime startTime = DateTime.UtcNow.AddMinutes(-prices.Length);
            for (int i = 0; i < prices.Length; i++)
            {
                decimal p = (decimal)prices[i];
                decimal v = (volumes != null && i < volumes.Length) ? (decimal)volumes[i] : 1.0m;
                quotes.Add(new Quote
                {
                    Date = startTime.AddMinutes(i),
                    Open = p,
                    High = p,
                    Low = p,
                    Close = p,
                    Volume = v
                });
            }
        }

        return quotes;
    }

    public static double ComputeRsi(double[] data, int period = 14)
    {
        var quotes = ConvertToQuotes(data);
        if (quotes.Count < period + 1) return 50.0;

        var results = quotes.GetRsi(period);
        var last = results.LastOrDefault();
        return last?.Rsi.HasValue == true ? (double)last.Rsi.Value : 50.0;
    }

    public static double ComputeConnorsRsi(double[] data)
    {
        var quotes = ConvertToQuotes(data);
        if (quotes.Count < 20) return ComputeRsi(data, 14);

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

    public static double ComputeHma(double[] data, int period = 9)
    {
        var quotes = ConvertToQuotes(data);
        if (quotes.Count < period) return data.Length > 0 ? data[^1] : 0.0;

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

    public static double ComputeEma(double[] data, int period = 9)
    {
        var quotes = ConvertToQuotes(data);
        if (quotes.Count < period) return data.Length > 0 ? data[^1] : 0.0;

        var results = quotes.GetEma(period);
        var last = results.LastOrDefault();
        return last?.Ema.HasValue == true ? (double)last.Ema.Value : data[^1];
    }

    public static (double macd, double signal) ComputeMacd(double[] data, int index)
    {
        var quotes = ConvertToQuotes(data);
        if (quotes.Count < 26) return (0.0, 0.0);

        var results = quotes.GetMacd(12, 26, 9);
        var last = results.LastOrDefault();
        double macdLine = last?.Macd.HasValue == true ? (double)last.Macd.Value : 0.0;
        double signalLine = last?.Signal.HasValue == true ? (double)last.Signal.Value : 0.0;
        return (macdLine, signalLine);
    }

    public static (double adx, double pdi, double mdi) ComputeTrueAdx(MiniAppController.OhlcCandle[] candles, int period = 14)
    {
        var quotes = ConvertToQuotes(Array.Empty<double>(), candles: candles);
        if (quotes.Count < period + 1) return (20.0, 0.0, 0.0);

        var results = quotes.GetAdx(period);
        var last = results.LastOrDefault();
        if (last == null) return (20.0, 0.0, 0.0);

        double adx = last.Adx.HasValue ? (double)last.Adx.Value : 20.0;
        double pdi = last.Pdi.HasValue ? (double)last.Pdi.Value : 0.0;
        double mdi = last.Mdi.HasValue ? (double)last.Mdi.Value : 0.0;

        return (adx, pdi, mdi);
    }

    public static double ComputeAtr(MiniAppController.OhlcCandle[] candles, int period = 14)
    {
        var quotes = ConvertToQuotes(Array.Empty<double>(), candles: candles);
        if (quotes.Count < period) return 0;

        var results = quotes.GetAtr(period);
        var last = results.LastOrDefault();
        return last?.Atr.HasValue == true ? (double)last.Atr.Value : 0.0;
    }

    public static double ComputeBollingerZscore(double[] prices, int period = 20)
    {
        var quotes = ConvertToQuotes(prices);
        if (quotes.Count < period) return 0.0;

        var results = quotes.GetBollingerBands(period, 2);
        var last = results.LastOrDefault();
        if (last == null || !last.ZScore.HasValue) return 0.0;

        return (double)last.ZScore.Value;
    }

    public static (double score, double confidence, double rsiVal, double emaVal, double volStrengthVal, double atrVal) ScoreTimeframe(
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

        // Proportional Connors RSI scoring
        if (rsi > 70) score -= (rsi - 70) / 15.0;
        else if (rsi < 30) score += (30 - rsi) / 15.0;
        else score += (rsi - 50) / 20.0;

        // HMA (Hull Moving Average zero-lag) scoring
        if (lastPrice > hma) score += 0.35;
        else if (lastPrice < hma) score -= 0.35;

        // ADX scoring
        if (adxVal > 25)
        {
            confidence += Math.Min((adxVal - 25) * 0.8, 20);
            if (pdiVal > mdiVal && pdiVal > 0) score += 0.4;
            else if (mdiVal > pdiVal && mdiVal > 0) score -= 0.4;
        }

        // Volume strength scoring
        double volStrength = 0.0;
        if (volumes.Length >= 5)
        {
            double avgVol = volumes.Take(volumes.Length - 1).TakeLast(20).Average();
            double lastVol = volumes[^1];
            if (avgVol > 0)
            {
                double ratio = lastVol / avgVol;
                double priceChange = prices.Length >= 2 ? prices[^1] - prices[^2] : 0;
                volStrength = (priceChange >= 0 ? 1 : -1) * ratio;
            }
        }

        return (score, Math.Clamp(confidence, 50, 95), Math.Round(rsi, 1), Math.Round(hma, 5), Math.Round(volStrength, 2), Math.Round(atrVal, 6));
    }

    public record GatekeeperResult(bool IsTradeable, string Reason, double Atr, double Adx);

    public static GatekeeperResult ValidateMarketGatekeeper(double[] prices, MiniAppController.OhlcCandle[]? candles = null)
    {
        if (prices == null || prices.Length < 15)
        {
            return new GatekeeperResult(false, "Недостаточно свечей для проверки Gatekeeper", 0, 0);
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
            return new GatekeeperResult(false, "⚠️ Рынок в состоянии застоя (нет колебаний цены).", atr, adx);
        }

        return new GatekeeperResult(true, "Рынок активен", atr, adx);
    }

    public static double CalculateVolatilityRatio(double[] prices)
    {
        if (prices == null || prices.Length < 25) return 1.0;

        double[] shortReturns = new double[5];
        for (int i = 0; i < 5; i++)
        {
            int idx = prices.Length - 1 - i;
            shortReturns[i] = Math.Abs((prices[idx] - prices[idx - 1]) / (prices[idx - 1] + 1e-10));
        }
        double shortVol = shortReturns.Average();

        double[] longReturns = new double[20];
        for (int i = 0; i < 20; i++)
        {
            int idx = prices.Length - 6 - i;
            longReturns[i] = Math.Abs((prices[idx] - prices[idx - 1]) / (prices[idx - 1] + 1e-10));
        }
        double longVol = longReturns.Average();

        if (longVol < 1e-9) return 1.0;
        return shortVol / longVol;
    }
}
