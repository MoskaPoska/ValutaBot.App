using System;
using System.Collections.Generic;

namespace ValutaBot.MiniApp.Indicators;

public class StatefulSmc
{
    public readonly record struct FvgZone(double Top, double Bottom, bool IsBullish, DateTime CreationTime);
    public readonly record struct OrderBlockZone(double Top, double Bottom, bool IsBullish, DateTime CreationTime);
    public readonly record struct Fractal(double Price, bool IsHigh, DateTime Time);

    private readonly List<FvgZone> _activeFvgs = new();
    private readonly List<OrderBlockZone> _activeObs = new();
    
    // Track the most recent fractals for BOS/CHOCH detection
    private Fractal _lastSwingHigh;
    private Fractal _lastSwingLow;
    private string _currentTrend = "NEUTRAL"; // "BULLISH", "BEARISH", "NEUTRAL"
    
    private DateTime _lastProcessedTime;
    
    private double _recentAtr;

    // Output state
    public bool HasLiquiditySweep { get; private set; }
    public string SweepDirection { get; private set; } = "NONE";
    
    public bool HasBos { get; private set; }
    public string BosDirection { get; private set; } = "NONE";

    public void Update(ReadOnlySpan<MiniAppController.OhlcCandle> candles, double currentPrice)
    {
        if (candles.Length < 5) return;
        
        // Reset transient single-tick events
        HasLiquiditySweep = false;
        SweepDirection = "NONE";
        HasBos = false;
        BosDirection = "NONE";

        // Calculate ATR for noise filtering
        CalculateAtr(candles);

        // Process only NEW closed candles to update zones/fractals
        for (int i = 2; i < candles.Length - 1; i++) // Leave the last candle out as it's not closed
        {
            var c = candles[i];
            if (c.Timestamp <= _lastProcessedTime && _lastProcessedTime != default)
                continue;

            // 1. Fractal Detection (requires 5 candles: i-2, i-1, i, i+1, i+2)
            // Wait, since we are at i, i+1 is the current forming candle. So we can only detect fractals at i-1 or i-2.
            // Let's detect fractals at i-2.
            if (i >= 4)
            {
                DetectFractalAt(candles, i - 2);
            }

            // 2. FVG Detection (requires 3 candles: i-2, i-1, i)
            DetectFvgAt(candles, i);

            // 3. OB Detection (we check if 'i' formed an OB by looking at subsequent displacement)
            // Actually, we can check if i-1 was an OB based on the strong displacement of i.
            DetectOrderBlockAt(candles, i - 1, i);

            // 4. BOS/CHOCH Detection based on closed candle 'i' breaking last fractals
            DetectStructureBreak(c);

            // 5. Mitigate zones
            MitigateZones(c);

            _lastProcessedTime = c.Timestamp;
        }

        // Live mitigation (check if the current open price taps any zones)
        MitigateZones(candles[^1]);
    }

    private void CalculateAtr(ReadOnlySpan<MiniAppController.OhlcCandle> candles)
    {
        int period = Math.Min(14, candles.Length - 1);
        if (period <= 0) return;
        
        double sum = 0;
        for (int i = candles.Length - 1 - period; i < candles.Length - 1; i++)
        {
            sum += (candles[i].High - candles[i].Low);
        }
        _recentAtr = sum / period;
    }

    private void DetectFractalAt(ReadOnlySpan<MiniAppController.OhlcCandle> candles, int idx)
    {
        var cM2 = candles[idx - 2];
        var cM1 = candles[idx - 1];
        var c = candles[idx];
        var cP1 = candles[idx + 1];
        var cP2 = candles[idx + 2];

        bool isSwingHigh = c.High > cM2.High && c.High > cM1.High && c.High > cP1.High && c.High > cP2.High;
        bool isSwingLow = c.Low < cM2.Low && c.Low < cM1.Low && c.Low < cP1.Low && c.Low < cP2.Low;

        if (isSwingHigh)
        {
            _lastSwingHigh = new Fractal(c.High, true, c.Timestamp);
            // Check for liquidity sweep
            if (cP2.High > c.High && cP2.Close < c.High)
            {
                HasLiquiditySweep = true;
                SweepDirection = "BEARISH_SWEEP";
            }
        }
        
        if (isSwingLow)
        {
            _lastSwingLow = new Fractal(c.Low, false, c.Timestamp);
            // Check for liquidity sweep
            if (cP2.Low < c.Low && cP2.Close > c.Low)
            {
                HasLiquiditySweep = true;
                SweepDirection = "BULLISH_SWEEP";
            }
        }
    }

    private void DetectFvgAt(ReadOnlySpan<MiniAppController.OhlcCandle> candles, int idx)
    {
        var c1 = candles[idx - 2];
        var c3 = candles[idx];
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
        var ob = candles[obIdx];
        var disp = candles[dispIdx];

        double body = Math.Abs(ob.Close - ob.Open);
        double range = ob.High - ob.Low;
        if (range <= 1e-8 || (body / range) < 0.60) return;

        bool isBearishCandle = ob.Close < ob.Open;
        bool isBullishCandle = ob.Close > ob.Open;

        // Bullish OB: Bearish candle before strong bullish displacement
        if (isBearishCandle && disp.Close > disp.Open && (disp.Close - disp.Open) > (_recentAtr * 0.4))
        {
            _activeObs.Add(new OrderBlockZone(ob.High, ob.Low, true, disp.Timestamp));
            if (_activeObs.Count > 5) _activeObs.RemoveAt(0);
        }
        // Bearish OB: Bullish candle before strong bearish displacement
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
                HasBos = true;
                BosDirection = "BULLISH_BOS"; // Or CHOCH
                _currentTrend = "BULLISH";
            }
        }
        else if (_lastSwingLow.Time != default && c.Close < _lastSwingLow.Price)
        {
            if (_currentTrend != "BEARISH")
            {
                HasBos = true;
                BosDirection = "BEARISH_BOS";
                _currentTrend = "BEARISH";
            }
        }
    }

    private void MitigateZones(MiniAppController.OhlcCandle c)
    {
        _activeFvgs.RemoveAll(f => 
            (f.IsBullish && c.Low <= f.Bottom) || 
            (!f.IsBullish && c.High >= f.Top));

        _activeObs.RemoveAll(o => 
            (o.IsBullish && c.Low <= o.Top) || 
            (!o.IsBullish && c.High >= o.Bottom));
    }

    public (bool hasBullishFvg, bool hasBearishFvg, FvgZone? nearestFvg) GetNearestFvg(double currentPrice)
    {
        bool bull = false, bear = false;
        FvgZone? nearest = null;
        double minDistance = double.MaxValue;

        foreach (var fvg in _activeFvgs)
        {
            if (fvg.IsBullish) bull = true;
            else bear = true;

            double dist = Math.Min(Math.Abs(currentPrice - fvg.Top), Math.Abs(currentPrice - fvg.Bottom));
            if (dist < minDistance)
            {
                minDistance = dist;
                nearest = fvg;
            }
        }
        return (bull, bear, nearest);
    }

    public (bool hasBullishOb, bool hasBearishOb, OrderBlockZone? nearestOb) GetNearestOb(double currentPrice)
    {
        bool bull = false, bear = false;
        OrderBlockZone? nearest = null;
        double minDistance = double.MaxValue;

        foreach (var ob in _activeObs)
        {
            if (ob.IsBullish) bull = true;
            else bear = true;

            double dist = Math.Min(Math.Abs(currentPrice - ob.Top), Math.Abs(currentPrice - ob.Bottom));
            if (dist < minDistance)
            {
                minDistance = dist;
                nearest = ob;
            }
        }
        return (bull, bear, nearest);
    }
}
