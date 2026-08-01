using System;
using System.Numerics;

namespace ValutaBot.MiniApp;

public class StatefulRsi
{
    private readonly int _period;
    private int _count;
    private double _avgGain;
    private double _avgLoss;
    private double _prevPrice;

    public StatefulRsi(int period = 14) { _period = period; }

    public double Update(double price)
    {
        if (_count == 0)
        {
            _prevPrice = price;
            _count++;
            return 50.0;
        }
        double diff = price - _prevPrice;
        _prevPrice = price;

        if (_count <= _period)
        {
            if (diff > 0) _avgGain += diff;
            else _avgLoss -= diff;
            if (_count == _period)
            {
                _avgGain /= _period;
                _avgLoss /= _period;
            }
            _count++;
            if (_count <= _period) return 50.0;
        }
        else
        {
            double gain = diff > 0 ? diff : 0;
            double loss = diff < 0 ? -diff : 0;
            _avgGain = (_avgGain * (_period - 1) + gain) / _period;
            _avgLoss = (_avgLoss * (_period - 1) + loss) / _period;
            _count++;
        }
        if (_avgLoss < 1e-10) return 100.0;
        return 100.0 - (100.0 / (1.0 + (_avgGain / _avgLoss)));
    }
}

public class StatefulConnorsRsi
{
    private readonly StatefulRsi _rsi = new(3);
    private readonly StatefulRsi _streakRsi = new(2);
    private double _currentStreak;
    private double _prevPrice;
    private int _count;
    private readonly int _rankPeriod = 10;
    private readonly double[] _returnsHistory;
    private int _returnsCount;

    public StatefulConnorsRsi() { _returnsHistory = new double[_rankPeriod]; }
    
    public double Update(double price)
    {
        double rsiVal = _rsi.Update(price);
        if (_count > 0)
        {
            if (price > _prevPrice) _currentStreak = _currentStreak > 0 ? _currentStreak + 1 : 1;
            else if (price < _prevPrice) _currentStreak = _currentStreak < 0 ? _currentStreak - 1 : -1;
            else _currentStreak = 0;
        }
        double streakRsiVal = _streakRsi.Update(_currentStreak);
        double currentReturn = 0;
        if (_count > 0 && Math.Abs(_prevPrice) > 1e-10) currentReturn = (price - _prevPrice) / _prevPrice;
        
        int winCount = 0;
        int totalRanked = 0;
        for (int i = 0; i < _returnsCount; i++)
        {
            if (currentReturn > _returnsHistory[i]) winCount++;
            totalRanked++;
        }
        double pctRank = totalRanked > 0 ? (winCount / (double)totalRanked) * 100.0 : 50.0;
        
        if (_returnsCount < _rankPeriod) 
        {
            _returnsHistory[_returnsCount] = currentReturn;
            _returnsCount++;
        }
        else
        {
            Array.Copy(_returnsHistory, 1, _returnsHistory, 0, _rankPeriod - 1);
            _returnsHistory[_rankPeriod - 1] = currentReturn;
        }
        
        _prevPrice = price;
        _count++;
        return (rsiVal + streakRsiVal + pctRank) / 3.0;
    }
}

public class StatefulHma
{
    private readonly int _period;
    private readonly int _halfPeriod;
    private readonly int _sqrtPeriod;
    private readonly double[] _priceHistory;
    private int _priceCount;
    private readonly double[] _diffHistory;
    private int _diffCount;

    public StatefulHma(int period = 9)
    {
        _period = period;
        _halfPeriod = period / 2;
        _sqrtPeriod = (int)Math.Sqrt(period);
        _priceHistory = new double[period];
        _diffHistory = new double[_sqrtPeriod];
    }
    
    public double Update(double price)
    {
        if (_priceCount < _period)
        {
            _priceHistory[_priceCount] = price;
            _priceCount++;
        }
        else
        {
            Array.Copy(_priceHistory, 1, _priceHistory, 0, _period - 1);
            _priceHistory[_period - 1] = price;
        }
        
        if (_priceCount == _period)
        {
            double wmaHalf = Wma(_priceHistory, _halfPeriod);
            double wmaFull = Wma(_priceHistory, _period);
            double diff = 2.0 * wmaHalf - wmaFull;
            
            if (_diffCount < _sqrtPeriod)
            {
                _diffHistory[_diffCount] = diff;
                _diffCount++;
            }
            else
            {
                Array.Copy(_diffHistory, 1, _diffHistory, 0, _sqrtPeriod - 1);
                _diffHistory[_sqrtPeriod - 1] = diff;
            }
            
            if (_diffCount == _sqrtPeriod)
            {
                return Wma(_diffHistory, _sqrtPeriod);
            }
        }
        return price;
    }
    
    private double Wma(double[] arr, int period)
    {
        double sum = 0;
        double weightSum = 0;
        int startIndex = arr.Length - period;
        for (int i = 0; i < period; i++)
        {
            double w = i + 1;
            sum += arr[startIndex + i] * w;
            weightSum += w;
        }
        return weightSum > 0 ? sum / weightSum : 0;
    }
}

public class StatefulEma
{
    private readonly int _period;
    private readonly double _k;
    private int _count;
    private double _ema;
    private double _sum;

    public StatefulEma(int period = 9)
    {
        _period = period;
        _k = 2.0 / (period + 1.0);
    }

    public double Update(double price)
    {
        if (_count < _period)
        {
            _sum += price;
            _count++;
            if (_count == _period) _ema = _sum / _period;
            return _count < _period ? price : _ema;
        }
        _ema = (price - _ema) * _k + _ema;
        _count++;
        return _ema;
    }
}

public class StatefulAtr
{
    private readonly int _period;
    private int _count;
    private double _atr;
    private double _prevClose;
    private double _sumTr;
    
    public double LastAtr { get; private set; }

    public StatefulAtr(int period = 14) { _period = period; }

    public double Update(double high, double low, double close)
    {
        if (_count == 0)
        {
            _prevClose = close;
            _count++;
            return 0.0;
        }
        double tr = Math.Max(high - low, Math.Max(Math.Abs(high - _prevClose), Math.Abs(low - _prevClose)));
        _prevClose = close;

        if (_count <= _period)
        {
            _sumTr += tr;
            if (_count == _period) _atr = _sumTr / _period;
            _count++;
            LastAtr = _count <= _period ? 0.0 : _atr;
            return LastAtr;
        }
        _atr = (_atr * (_period - 1) + tr) / _period;
        _count++;
        LastAtr = _atr;
        return _atr;
    }
}

public class StatefulTrueAdx
{
    private readonly int _period;
    private int _count;
    private double _prevClose, _prevHigh, _prevLow;
    private double _smoothTr, _smoothPdm, _smoothMdm;
    private double _adx;
    private readonly double[] _dxHistory;
    private double _sumDx;

    public double LastPdi { get; private set; }
    public double LastMdi { get; private set; }
    public double LastAdx { get; private set; }

    public StatefulTrueAdx(int period = 14)
    {
        _period = period;
        _dxHistory = new double[period];
    }

    public double Update(double high, double low, double close)
    {
        if (_count == 0)
        {
            _prevClose = close; _prevHigh = high; _prevLow = low;
            _count++; return 20.0;
        }

        double tr = Math.Max(high - low, Math.Max(Math.Abs(high - _prevClose), Math.Abs(low - _prevClose)));
        double upMove = high - _prevHigh;
        double downMove = _prevLow - low;
        
        double pdm = (upMove > downMove && upMove > 0) ? upMove : 0;
        double mdm = (downMove > upMove && downMove > 0) ? downMove : 0;

        _prevClose = close; _prevHigh = high; _prevLow = low;

        if (_count <= _period)
        {
            _smoothTr += tr; _smoothPdm += pdm; _smoothMdm += mdm;
            _count++; return 20.0;
        }

        if (_count > _period + 1)
        {
            _smoothTr = _smoothTr - (_smoothTr / _period) + tr;
            _smoothPdm = _smoothPdm - (_smoothPdm / _period) + pdm;
            _smoothMdm = _smoothMdm - (_smoothMdm / _period) + mdm;
        }

        LastPdi = _smoothTr == 0 ? 0 : 100.0 * _smoothPdm / _smoothTr;
        LastMdi = _smoothTr == 0 ? 0 : 100.0 * _smoothMdm / _smoothTr;
        double dx = (LastPdi + LastMdi) == 0 ? 0 : 100.0 * Math.Abs(LastPdi - LastMdi) / (LastPdi + LastMdi);

        if (_count <= _period * 2)
        {
            _dxHistory[_count - _period - 1] = dx;
            if (_count == _period * 2)
            {
                for(int i=0; i<_period; i++) _sumDx += _dxHistory[i];
                _adx = _sumDx / _period;
            }
        }
        else
        {
            _adx = (_adx * (_period - 1) + dx) / _period;
        }

        _count++;
        LastAdx = _count <= _period * 2 ? 20.0 : _adx;
        return LastAdx;
    }
}

public class TechnicalAnalysisEngine : ITechnicalAnalysisEngine
{
    public static ITechnicalAnalysisEngine Instance { get; set; } = new TechnicalAnalysisEngine();

    private class CacheState
    {
        public StatefulRsi Rsi;
        public long RsiLastTick;
        public double RsiLast;
        public StatefulConnorsRsi ConnorsRsi;
        public long ConnorsRsiLastTick;
        public double ConnorsRsiLast;
        public StatefulHma Hma;
        public long HmaLastTick;
        public double HmaLast;
        public StatefulEma Ema;
        public long EmaLastTick;
        public double EmaLast;
        public StatefulTrueAdx Adx;
        public long AdxLastTick;
        public StatefulAtr Atr;
        public long AtrLastTick;
    }
    private readonly System.Collections.Concurrent.ConcurrentDictionary<(string, string), CacheState> _cache = new();

    public double ComputeRsi(string asset, string timeframe, ReadOnlySpan<MiniAppController.OhlcCandle> candles, int period = 14)
    {
        if (candles.Length <= period) return 50.0;
        var state = _cache.GetOrAdd((asset, timeframe), _ => new CacheState());
        lock (state)
        {
            int unseenCount = 0;
            for (int i = candles.Length - 1; i >= 0; i--) {
                if (candles[i].Timestamp.Ticks <= state.RsiLastTick) break;
                unseenCount++;
            }
            if (state.Rsi == null || unseenCount > 10 || (candles.Length > 0 && candles[^1].Timestamp.Ticks < state.RsiLastTick))
            {
                state.Rsi = new StatefulRsi(period);
                state.RsiLast = 50.0;
                for (int i = 0; i < candles.Length; i++) state.RsiLast = state.Rsi.Update(candles[i].Close);
                state.RsiLastTick = candles[^1].Timestamp.Ticks;
            }
            else if (unseenCount > 0)
            {
                for (int i = candles.Length - unseenCount; i < candles.Length; i++)
                    state.RsiLast = state.Rsi.Update(candles[i].Close);
                state.RsiLastTick = candles[^1].Timestamp.Ticks;
            }
            return state.RsiLast;
        }
    }

    public double ComputeConnorsRsi(string asset, string timeframe, ReadOnlySpan<MiniAppController.OhlcCandle> candles)
    {
        if (candles.Length < 20) return ComputeRsi(asset, timeframe, candles, 14);
        var state = _cache.GetOrAdd((asset, timeframe), _ => new CacheState());
        lock (state)
        {
            int unseenCount = 0;
            for (int i = candles.Length - 1; i >= 0; i--) {
                if (candles[i].Timestamp.Ticks <= state.ConnorsRsiLastTick) break;
                unseenCount++;
            }
            if (state.ConnorsRsi == null || unseenCount > 10 || (candles.Length > 0 && candles[^1].Timestamp.Ticks < state.ConnorsRsiLastTick))
            {
                state.ConnorsRsi = new StatefulConnorsRsi();
                state.ConnorsRsiLast = 50.0;
                for (int i = 0; i < candles.Length; i++) state.ConnorsRsiLast = state.ConnorsRsi.Update(candles[i].Close);
                state.ConnorsRsiLastTick = candles[^1].Timestamp.Ticks;
            }
            else if (unseenCount > 0)
            {
                for (int i = candles.Length - unseenCount; i < candles.Length; i++)
                    state.ConnorsRsiLast = state.ConnorsRsi.Update(candles[i].Close);
                state.ConnorsRsiLastTick = candles[^1].Timestamp.Ticks;
            }
            return state.ConnorsRsiLast;
        }
    }

    public double ComputeHma(string asset, string timeframe, ReadOnlySpan<MiniAppController.OhlcCandle> candles, int period = 9)
    {
        if (candles.Length < period) return candles.Length > 0 ? candles[^1].Close : 0.0;
        var state = _cache.GetOrAdd((asset, timeframe), _ => new CacheState());
        lock (state)
        {
            int unseenCount = 0;
            for (int i = candles.Length - 1; i >= 0; i--) {
                if (candles[i].Timestamp.Ticks <= state.HmaLastTick) break;
                unseenCount++;
            }
            if (state.Hma == null || unseenCount > 10 || (candles.Length > 0 && candles[^1].Timestamp.Ticks < state.HmaLastTick))
            {
                state.Hma = new StatefulHma(period);
                state.HmaLast = 0.0;
                for (int i = 0; i < candles.Length; i++) state.HmaLast = state.Hma.Update(candles[i].Close);
                state.HmaLastTick = candles[^1].Timestamp.Ticks;
            }
            else if (unseenCount > 0)
            {
                for (int i = candles.Length - unseenCount; i < candles.Length; i++)
                    state.HmaLast = state.Hma.Update(candles[i].Close);
                state.HmaLastTick = candles[^1].Timestamp.Ticks;
            }
            return state.HmaLast;
        }
    }

    public double ComputeEma(string asset, string timeframe, ReadOnlySpan<MiniAppController.OhlcCandle> candles, int period = 9)
    {
        if (candles.Length == 0) return 0.0;
        var state = _cache.GetOrAdd((asset, timeframe), _ => new CacheState());
        lock (state)
        {
            int unseenCount = 0;
            for (int i = candles.Length - 1; i >= 0; i--) {
                if (candles[i].Timestamp.Ticks <= state.EmaLastTick) break;
                unseenCount++;
            }
            if (state.Ema == null || unseenCount > 10 || (candles.Length > 0 && candles[^1].Timestamp.Ticks < state.EmaLastTick))
            {
                state.Ema = new StatefulEma(period);
                state.EmaLast = 0.0;
                for (int i = 0; i < candles.Length; i++) state.EmaLast = state.Ema.Update(candles[i].Close);
                state.EmaLastTick = candles[^1].Timestamp.Ticks;
            }
            else if (unseenCount > 0)
            {
                for (int i = candles.Length - unseenCount; i < candles.Length; i++)
                    state.EmaLast = state.Ema.Update(candles[i].Close);
                state.EmaLastTick = candles[^1].Timestamp.Ticks;
            }
            return state.EmaLast;
        }
    }

    public (double adx, double pdi, double mdi) ComputeTrueAdx(string asset, string timeframe, ReadOnlySpan<MiniAppController.OhlcCandle> candles, int period = 14)
    {
        if (candles.Length <= period) return (20.0, 0.0, 0.0);
        var state = _cache.GetOrAdd((asset, timeframe), _ => new CacheState());
        lock (state)
        {
            int unseenCount = 0;
            for (int i = candles.Length - 1; i >= 0; i--) {
                if (candles[i].Timestamp.Ticks <= state.AdxLastTick) break;
                unseenCount++;
            }
            if (state.Adx == null || unseenCount > 10 || (candles.Length > 0 && candles[^1].Timestamp.Ticks < state.AdxLastTick))
            {
                state.Adx = new StatefulTrueAdx(period);
                for (int i = 0; i < candles.Length; i++) state.Adx.Update(candles[i].High, candles[i].Low, candles[i].Close);
                state.AdxLastTick = candles[^1].Timestamp.Ticks;
            }
            else if (unseenCount > 0)
            {
                for (int i = candles.Length - unseenCount; i < candles.Length; i++)
                    state.Adx.Update(candles[i].High, candles[i].Low, candles[i].Close);
                state.AdxLastTick = candles[^1].Timestamp.Ticks;
            }
            return (state.Adx.LastAdx, state.Adx.LastPdi, state.Adx.LastMdi); 
        }
    }

    public double ComputeAtr(string asset, string timeframe, ReadOnlySpan<MiniAppController.OhlcCandle> candles, int period = 14)
    {
        if (candles.Length <= period) return 0.0;
        var state = _cache.GetOrAdd((asset, timeframe), _ => new CacheState());
        lock (state)
        {
            int unseenCount = 0;
            for (int i = candles.Length - 1; i >= 0; i--) {
                if (candles[i].Timestamp.Ticks <= state.AtrLastTick) break;
                unseenCount++;
            }
            if (state.Atr == null || unseenCount > 10 || (candles.Length > 0 && candles[^1].Timestamp.Ticks < state.AtrLastTick))
            {
                state.Atr = new StatefulAtr(period);
                for (int i = 0; i < candles.Length; i++) state.Atr.Update(candles[i].High, candles[i].Low, candles[i].Close);
                state.AtrLastTick = candles[^1].Timestamp.Ticks;
            }
            else if (unseenCount > 0)
            {
                for (int i = candles.Length - unseenCount; i < candles.Length; i++)
                    state.Atr.Update(candles[i].High, candles[i].Low, candles[i].Close);
                state.AtrLastTick = candles[^1].Timestamp.Ticks;
            }
            return state.Atr.LastAtr;
        }
    }

    public (double score, double confidence, double rsiVal, double emaVal, double volStrengthVal, double atrVal) ScoreTimeframe(
        string asset, string timeframe, ReadOnlySpan<double> prices, ReadOnlySpan<double> volumes, ReadOnlySpan<MiniAppController.OhlcCandle> candles = default,
        double? adxOverride = null, double? atrOverride = null, bool isForex = false)
    {
        if (prices.Length < 14 || candles.Length < 14) return (0.0, 50.0, 50.0, 0.0, 0.0, 0.0);

        double rsi = ComputeConnorsRsi(asset, timeframe, candles);
        double hma = ComputeHma(asset, timeframe, candles, 9);
        double lastPrice = prices[^1];

        var (adxVal, pdiVal, mdiVal) = adxOverride.HasValue
            ? (adxOverride.Value, 0.0, 0.0)
            : (candles.Length > 0 ? ComputeTrueAdx(asset, timeframe, candles) : (20.0, 0.0, 0.0));

        double atrVal = atrOverride.HasValue
            ? atrOverride.Value
            : (candles.Length > 0 ? ComputeAtr(asset, timeframe, candles) : 0);

        double score = 0;
        double confidence = 60.0;

        score += (rsi - 50.0) / 40.0;

        if (lastPrice > hma) score += 0.15;
        else if (lastPrice < hma) score -= 0.15;

        if (adxVal > 25.0)
        {
            confidence += Math.Min((adxVal - 25.0) * 0.8, 20.0);
            if (pdiVal > mdiVal && pdiVal > 0) score += 0.25;
            else if (mdiVal > pdiVal && mdiVal > 0) score -= 0.25;
        }

        double volStrength = 0.0;
        if (volumes.Length >= 5)
        {
            int volCount = 0;
            double volSum = 0;
            int startIdx = Math.Max(0, volumes.Length - 21);
            for (int i = startIdx; i < volumes.Length - 1; i++)
            {
                volSum += volumes[i];
                volCount++;
            }
            double avgVol = volCount > 0 ? volSum / volCount : 0.0;
            double lastVol = volumes[^1];
            if (avgVol > 1e-9)
            {
                double ratio = lastVol / avgVol;
                double priceChange = prices.Length >= 2 ? prices[^1] - prices[^2] : 0.0;
                volStrength = (priceChange >= 0 ? 1.0 : -1.0) * Math.Max(0.0, Math.Min(ratio - 1.0, 1.0));
                score += Math.Clamp(volStrength * 0.15, -0.20, 0.20);
            }
        }

        return (score, Math.Clamp(confidence, 50.0, 95.0), Math.Round(rsi, 1), Math.Round(hma, 5), Math.Round(volStrength, 2), Math.Round(atrVal, 6));
    }

    public record GatekeeperResult(bool IsTradeable, string Reason, double Atr, double Adx);

    public GatekeeperResult ValidateMarketGatekeeper(string asset, string timeframe, ReadOnlySpan<double> prices, ReadOnlySpan<MiniAppController.OhlcCandle> candles = default)
    {
        if (prices.Length < 15) return new GatekeeperResult(false, "Недостаточно данных цены для проверки Gatekeeper", 0, 0);

        double atr = candles.Length >= 15 ? ComputeAtr(asset, timeframe, candles) : 0;
        var (adx, _, _) = candles.Length >= 15 ? ComputeTrueAdx(asset, timeframe, candles) : (20.0, 0, 0);

        double minPrice = double.MaxValue;
        double maxPrice = double.MinValue;
        int startIdx = prices.Length - 15;
        for (int i = startIdx; i < prices.Length; i++)
        {
            if (prices[i] < minPrice) minPrice = prices[i];
            if (prices[i] > maxPrice) maxPrice = prices[i];
        }
        
        double priceRange = maxPrice - minPrice;
        double deadMarketThreshold = Math.Max(1e-10, atr * 0.10);

        if (priceRange < deadMarketThreshold)
        {
            BotLogger.Warn($"[Gatekeeper] Market is completely flat / frozen. PriceRange={priceRange}, Threshold={deadMarketThreshold}. Aborting analysis.");
            return new GatekeeperResult(false, "⚠️ Рынок в состоянии застоя (нет колебаний цены).", atr, adx);
        }

        double maxCandleRange = 0;
        if (candles.Length > 0)
        {
            int cStartIdx = Math.Max(0, candles.Length - 3);
            for (int i = cStartIdx; i < candles.Length; i++)
            {
                double range = candles[i].High - candles[i].Low;
                if (range > maxCandleRange) maxCandleRange = range;
            }
        }
        
        if (atr > 0 && maxCandleRange > atr * 4.0)
        {
            BotLogger.Warn($"[Gatekeeper] Market Flash Crash detected! Single candle range {maxCandleRange} is > 4x ATR {atr}.");
            return new GatekeeperResult(false, "⚠️ Обнаружен аномальный выброс волатильности (Сквиз/Flash Crash). Торговля приостановлена для защиты депозита.", atr, adx);
        }

        return new GatekeeperResult(true, "Рынок активен", atr, adx);
    }

    public double CalculateVolatilityRatio(ReadOnlySpan<double> prices)
    {
        if (prices.Length < 26) return 1.0;

        Span<double> returns = stackalloc double[25];
        for (int i = 0; i < 25; i++)
        {
            int idx = prices.Length - 25 + i;
            returns[i] = Math.Log(prices[idx] / (prices[idx - 1] == 0 ? 1e-10 : prices[idx - 1]));
        }

        double shortVol = StandardDeviationScalar(returns.Slice(20, 5));
        double longVol = StandardDeviationScalar(returns.Slice(0, 20));

        if (longVol < 1e-10) return 1.0;
        return shortVol / longVol;
    }

    private static double StandardDeviationScalar(ReadOnlySpan<double> values)
    {
        int count = values.Length;
        if (count < 2) return 0.0;
        
        double sum = 0;
        for (int i = 0; i < count; i++) sum += values[i];
        double mean = sum / count;
        
        double sqSum = 0;
        for (int i = 0; i < count; i++)
        {
            double diff = values[i] - mean;
            sqSum += diff * diff;
        }
        
        return Math.Sqrt(sqSum / (count - 1));
    }
}
