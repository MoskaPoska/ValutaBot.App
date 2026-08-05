using System;
using System.Collections.Generic;

namespace ValutaBot.MiniApp.Indicators;

/// <summary>
/// Thread-safe stateful Smart Money Concepts indicator.
///
/// FIX: All public methods and the Update method now use a shared _lockObj.
/// Previously _activeFvgs and _activeObs were modified by Update() while
/// GetNearestFvg/GetNearestOb iterated them concurrently, causing
/// InvalidOperationException ("Collection was modified").
/// </summary>
public class StatefulSmc
{
    public readonly record struct FvgZone(double Top, double Bottom, bool IsBullish, DateTime CreationTime);
    public readonly record struct OrderBlockZone(double Top, double Bottom, bool IsBullish, DateTime CreationTime);
    public readonly record struct Fractal(double Price, bool IsHigh, DateTime Time);

    private readonly object _lockObj = new();

    private readonly List<FvgZone>        _activeFvgs = new();
    private readonly List<OrderBlockZone> _activeObs  = new();

    private Fractal _lastSwingHigh;
    private Fractal _lastSwingLow;
    private string  _currentTrend = "NEUTRAL";

    private DateTime _lastProcessedTime;
    private double   _recentAtr;

    // Output state — backing fields accessed under lock
    private bool   _hasLiquiditySweep;
    private string _sweepDirection = "NONE";
    private bool   _hasBos;
    private string _bosDirection   = "NONE";

    public bool   HasLiquiditySweep { get { lock (_lockObj) { return _hasLiquiditySweep; } } }
    public string SweepDirection    { get { lock (_lockObj) { return _sweepDirection;    } } }
    public bool   HasBos            { get { lock (_lockObj) { return _hasBos;            } } }
    public string BosDirection      { get { lock (_lockObj) { return _bosDirection;      } } }

    public void Update(ReadOnlySpan<MiniAppController.OhlcCandle> candles, double currentPrice)
    {
        if (candles.Length < 5) return;

        lock (_lockObj)
        {
            // Reset transient single-tick events
            _hasLiquiditySweep = false;
            _sweepDirection    = "NONE";
            _hasBos            = false;
            _bosDirection      = "NONE";

            for (int i = 2; i < candles.Length - 1; i++)
            {
                var c = candles[i];
                if (c.Timestamp <= _lastProcessedTime && _lastProcessedTime != default)
                    continue;

                _recentAtr = GetAtrAt(candles, i, 14); // B13-FIX: Point-in-time ATR, no lookahead bias

                if (i >= 4) DetectFractalAt(candles, i - 2);
                DetectFvgAt(candles, i);
                DetectOrderBlockAt(candles, i - 1, i);
                DetectStructureBreak(c);
                DetectLiquiditySweep(c);  // FIX: sweep detection moved outside fractal block
                MitigateZones(c);

                _lastProcessedTime = c.Timestamp;
            }

            // Live mitigation on current open candle
            MitigateZones(candles[^1]);
        }
    }

    private double GetAtrAt(ReadOnlySpan<MiniAppController.OhlcCandle> candles, int currentIdx, int period)
    {
        int count = 0;
        double sum = 0;
        for (int i = currentIdx - period; i < currentIdx; i++)
        {
            if (i >= 0)
            {
                sum += (candles[i].High - candles[i].Low);
                count++;
            }
        }
        return count > 0 ? sum / count : 0;
    }

    private void DetectFractalAt(ReadOnlySpan<MiniAppController.OhlcCandle> candles, int idx)
    {
        var cM2 = candles[idx - 2];
        var cM1 = candles[idx - 1];
        var c   = candles[idx];
        var cP1 = candles[idx + 1];
        var cP2 = candles[idx + 2];

        bool isSwingHigh = c.High > cM2.High && c.High > cM1.High && c.High > cP1.High && c.High > cP2.High;
        bool isSwingLow  = c.Low  < cM2.Low  && c.Low  < cM1.Low  && c.Low  < cP1.Low  && c.Low  < cP2.Low;

        // Record swing fractals only — sweep detection moved to DetectLiquiditySweep()
        if (isSwingHigh)
            _lastSwingHigh = new Fractal(c.High, true, c.Timestamp);

        if (isSwingLow)
            _lastSwingLow = new Fractal(c.Low, false, c.Timestamp);
    }

    /// <summary>
    /// Detects liquidity sweeps: current candle's wick exceeded a prior swing,
    /// but price rejected and closed back inside — classic stop-hunt pattern.
    /// FIX: Previously this logic was inside DetectFractalAt() inside the isSwingHigh
    /// block, checking cP2.High > c.High — which is mathematically impossible
    /// (isSwingHigh requires c.High > cP2.High). Sweep was NEVER detected.
    /// </summary>
    private void DetectLiquiditySweep(MiniAppController.OhlcCandle c)
    {
        // Bearish sweep: wick above prior swing high, but close back below it
        if (_lastSwingHigh.Time != default
            && c.High  > _lastSwingHigh.Price
            && c.Close < _lastSwingHigh.Price)
        {
            _hasLiquiditySweep = true;
            _sweepDirection    = "BEARISH_SWEEP";
        }

        // Bullish sweep: wick below prior swing low, but close back above it
        if (_lastSwingLow.Time != default
            && c.Low   < _lastSwingLow.Price
            && c.Close > _lastSwingLow.Price)
        {
            _hasLiquiditySweep = true;
            _sweepDirection    = "BULLISH_SWEEP";
        }
    }

    private void DetectFvgAt(ReadOnlySpan<MiniAppController.OhlcCandle> candles, int idx)
    {
        var    c1     = candles[idx - 2];
        var    c3     = candles[idx];
        double minGap = _recentAtr * 0.20;

        if (c3.Low > c1.High && (c3.Low - c1.High) >= minGap)
        {
            _activeFvgs.Add(new FvgZone(c3.Low, c1.High, true, c3.Timestamp));
            if (_activeFvgs.Count > 10) _activeFvgs.RemoveAt(0);
        }
        else if (c3.High < c1.Low && (c1.Low - c3.High) >= minGap)
        {
            _activeFvgs.Add(new FvgZone(c1.Low, c3.High, false, c3.Timestamp));
            if (_activeFvgs.Count > 10) _activeFvgs.RemoveAt(0);
        }
    }

    private void DetectOrderBlockAt(ReadOnlySpan<MiniAppController.OhlcCandle> candles, int obIdx, int dispIdx)
    {
        var ob   = candles[obIdx];
        var disp = candles[dispIdx];

        double body  = Math.Abs(ob.Close - ob.Open);
        double range = ob.High - ob.Low;
        if (range <= 1e-8 || (body / range) < 0.60) return;

        bool isBearishCandle = ob.Close < ob.Open;
        bool isBullishCandle = ob.Close > ob.Open;

        if (isBearishCandle && disp.Close > disp.Open && (disp.Close - disp.Open) > (_recentAtr * 0.4))
        {
            _activeObs.Add(new OrderBlockZone(ob.High, ob.Low, true, disp.Timestamp));
            if (_activeObs.Count > 5) _activeObs.RemoveAt(0);
        }
        else if (isBullishCandle && disp.Close < disp.Open && (disp.Open - disp.Close) > (_recentAtr * 0.4))
        {
            _activeObs.Add(new OrderBlockZone(ob.High, ob.Low, false, disp.Timestamp));
            if (_activeObs.Count > 5) _activeObs.RemoveAt(0);
        }
    }

    private void DetectStructureBreak(MiniAppController.OhlcCandle c)
    {
        if (_lastSwingHigh.Time != default && c.Close > _lastSwingHigh.Price)
        {
            if (_currentTrend != "BULLISH")
            {
                _hasBos       = true;
                _bosDirection = "BULLISH_BOS";
                _currentTrend = "BULLISH";
            }
        }
        else if (_lastSwingLow.Time != default && c.Close < _lastSwingLow.Price)
        {
            if (_currentTrend != "BEARISH")
            {
                _hasBos       = true;
                _bosDirection = "BEARISH_BOS";
                _currentTrend = "BEARISH";
            }
        }
    }

    private void MitigateZones(MiniAppController.OhlcCandle c)
    {
        _activeFvgs.RemoveAll(f =>
            (f.IsBullish  && c.Low  <= f.Bottom) ||
            (!f.IsBullish && c.High >= f.Top));

        _activeObs.RemoveAll(o =>
            (o.IsBullish  && c.Low  <= o.Top) ||
            (!o.IsBullish && c.High >= o.Bottom));
    }

    public (bool hasBullishFvg, bool hasBearishFvg, FvgZone? nearestFvg) GetNearestFvg(double currentPrice)
    {
        lock (_lockObj)
        {
            bool  bull        = false;
            bool  bear        = false;
            FvgZone? nearest  = null;
            double minDistance = double.MaxValue;

            foreach (var fvg in _activeFvgs)
            {
                if (fvg.IsBullish) bull = true;
                else               bear = true;

                double dist = Math.Min(
                    Math.Abs(currentPrice - fvg.Top),
                    Math.Abs(currentPrice - fvg.Bottom));
                if (dist < minDistance)
                {
                    minDistance = dist;
                    nearest     = fvg;
                }
            }
            return (bull, bear, nearest);
        }
    }

    public (bool hasBullishOb, bool hasBearishOb, OrderBlockZone? nearestOb) GetNearestOb(double currentPrice)
    {
        lock (_lockObj)
        {
            bool  bull        = false;
            bool  bear        = false;
            OrderBlockZone? nearest = null;
            double minDistance = double.MaxValue;

            foreach (var ob in _activeObs)
            {
                if (ob.IsBullish) bull = true;
                else              bear = true;

                double dist = Math.Min(
                    Math.Abs(currentPrice - ob.Top),
                    Math.Abs(currentPrice - ob.Bottom));
                if (dist < minDistance)
                {
                    minDistance = dist;
                    nearest     = ob;
                }
            }
            return (bull, bear, nearest);
        }
    }
}
