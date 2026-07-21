namespace ValutaBot.MiniApp;

/// <summary>
/// Technical analysis engine for RSI, EMA, MACD, ADX, ATR, DeMark, Hurst Exponent, and Volatility Ratio calculations.
/// </summary>
public static class TechnicalAnalysisEngine
{
    public static double ComputeRsi(double[] data, int period = 14)
    {
        if (data.Length < period + 1) return 50.0;
        double gains = 0, losses = 0;
        for (int i = data.Length - period; i < data.Length; i++)
        {
            double diff = data[i] - data[i - 1];
            if (diff >= 0) gains += diff;
            else losses -= diff;
        }
        if (losses == 0) return 100.0;
        double rs = gains / losses;
        return 100.0 - (100.0 / (1.0 + rs));
    }

    public static double ComputeEma(double[] data, int period)
    {
        if (data.Length < period) return data.Length > 0 ? data[^1] : 0.0;
        double multiplier = 2.0 / (period + 1);
        double ema = data[0];
        for (int i = 1; i < data.Length; i++)
        {
            ema = (data[i] - ema) * multiplier + ema;
        }
        return ema;
    }

    public static (double macd, double signal) ComputeMacd(double[] data, int index)
    {
        if (data.Length < 26) return (0.0, 0.0);
        int subLen = Math.Min(index + 1, data.Length);
        var sub = data[..subLen];

        double ema12 = ComputeEma(sub, 12);
        double ema26 = ComputeEma(sub, 26);
        double macdLine = ema12 - ema26;

        int macdHistLen = Math.Max(1, subLen - 26 + 1);
        var macdHist = new double[macdHistLen];
        for (int i = 0; i < macdHistLen; i++)
        {
            int idx = 26 - 1 + i;
            if (idx < subLen)
            {
                var slice = sub[..(idx + 1)];
                macdHist[i] = ComputeEma(slice, 12) - ComputeEma(slice, 26);
            }
        }

        double signalLine = ComputeEma(macdHist, 9);
        return (macdLine, signalLine);
    }

    public static (double adx, double pdi, double mdi) ComputeTrueAdx(MiniAppController.OhlcCandle[] candles, int period = 14)
    {
        int n = candles.Length;
        if (n < period + 1) return (20.0, 0.0, 0.0);

        var tr = new double[n];
        var pdm = new double[n];
        var mdm = new double[n];

        for (int i = 1; i < n; i++)
        {
            double h = candles[i].High, l = candles[i].Low;
            double pH = candles[i - 1].High, pL = candles[i - 1].Low, pC = candles[i - 1].Close;

            tr[i] = Math.Max(h - l, Math.Max(Math.Abs(h - pC), Math.Abs(l - pC)));

            double upMove = h - pH;
            double downMove = pL - l;

            pdm[i] = (upMove > downMove && upMove > 0) ? upMove : 0;
            mdm[i] = (downMove > upMove && downMove > 0) ? downMove : 0;
        }

        double smoothTr = 0, smoothPdm = 0, smoothMdm = 0;
        for (int i = 1; i <= period; i++)
        {
            smoothTr += tr[i];
            smoothPdm += pdm[i];
            smoothMdm += mdm[i];
        }

        var dx = new List<double>();
        double lastPdi = 0, lastMdi = 0;

        for (int i = period + 1; i < n; i++)
        {
            smoothTr = smoothTr - (smoothTr / period) + tr[i];
            smoothPdm = smoothPdm - (smoothPdm / period) + pdm[i];
            smoothMdm = smoothMdm - (smoothMdm / period) + mdm[i];

            if (smoothTr < 1e-12) continue;

            lastPdi = (smoothPdm / smoothTr) * 100.0;
            lastMdi = (smoothMdm / smoothTr) * 100.0;

            double diDiff = Math.Abs(lastPdi - lastMdi);
            double diSum = lastPdi + lastMdi;
            if (diSum > 1e-12)
            {
                dx.Add((diDiff / diSum) * 100.0);
            }
        }

        if (dx.Count == 0) return (20.0, lastPdi, lastMdi);

        double adx = dx.Count >= period ? dx.TakeLast(period).Average() : dx.Average();
        return (adx, lastPdi, lastMdi);
    }

    public static double ComputeAtr(MiniAppController.OhlcCandle[] candles, int period = 14)
    {
        int n = candles.Length;
        if (n < 2) return 0;
        var trs = new double[n - 1];
        for (int i = 1; i < n; i++)
        {
            double h = candles[i].High, l = candles[i].Low, pC = candles[i - 1].Close;
            trs[i - 1] = Math.Max(h - l, Math.Max(Math.Abs(h - pC), Math.Abs(l - pC)));
        }
        int count = Math.Min(period, trs.Length);
        return trs.TakeLast(count).Average();
    }

    public static double ComputeBollingerZscore(double[] prices, int period = 20)
    {
        int n = prices.Length;
        if (n < period) return 0.0;

        var slice = prices[^period..];
        double mean = slice.Average();
        double variance = slice.Sum(p => Math.Pow(p - mean, 2)) / period;
        double std = Math.Sqrt(variance);

        if (std < 1e-12) return 0.0;
        return (prices[^1] - mean) / std;
    }

    public static (double score, double confidence, double rsiVal, double emaVal, double volStrengthVal, double atrVal) ScoreTimeframe(
        double[] prices, double[] volumes, MiniAppController.OhlcCandle[]? candles = null,
        double? adxOverride = null, double? atrOverride = null, bool isForex = false)
    {
        if (prices.Length < 14) return (0.0, 50.0, 50.0, 0.0, 0.0, 0.0);

        double rsi = ComputeRsi(prices, 14);
        double ema = ComputeEma(prices, 9);
        double lastPrice = prices[^1];

        var (adxVal, pdiVal, mdiVal) = adxOverride.HasValue
            ? (adxOverride.Value, 0.0, 0.0)
            : (candles != null ? ComputeTrueAdx(candles) : (20.0, 0.0, 0.0));

        double atrVal = atrOverride.HasValue
            ? atrOverride.Value
            : (candles != null ? ComputeAtr(candles) : 0);

        double score = 0;
        double confidence = 60.0;

        // Proportional RSI scoring
        if (rsi > 70) score -= (rsi - 70) / 15.0;
        else if (rsi < 30) score += (30 - rsi) / 15.0;
        else score += (rsi - 50) / 20.0;

        // EMA scoring
        if (lastPrice > ema) score += 0.3;
        else if (lastPrice < ema) score -= 0.3;

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

        return (score, Math.Clamp(confidence, 50, 95), Math.Round(rsi, 1), Math.Round(ema, 5), Math.Round(volStrength, 2), Math.Round(atrVal, 6));
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
