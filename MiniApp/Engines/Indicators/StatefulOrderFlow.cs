using System;
using System.Collections.Generic;

namespace ValutaBot.MiniApp.Indicators;

public class StatefulOrderFlow
{
    private double _cumulativeVolumeDelta = 0;
    
    // Noise thresholds based on SMA of volume
    private double _volSum = 0;
    private int _volCount = 0;
    private double _avgVolume = 1.0;

    private DateTime _lastProcessedTime;

    // We keep a small window of the last 12 processed candles to calculate short-term DeltaRatio
    private readonly Queue<(double buy, double sell)> _shortTermWindow = new();
    private double _shortTermBuyVolume = 0;
    private double _shortTermSellVolume = 0;

    public double CumulativeVolumeDelta => _cumulativeVolumeDelta;
    
    // The DeltaRatio is calculated over the short-term window (12 candles), as it reflects immediate momentum
    public double DeltaRatio 
    { 
        get 
        {
            double sell = _shortTermSellVolume > 1e-8 ? _shortTermSellVolume : 1.0;
            return _shortTermBuyVolume / sell;
        } 
    }
    
    public double BuyVolume => _shortTermBuyVolume;
    public double SellVolume => _shortTermSellVolume;

    public bool HasInstitutionalBlockTrade { get; private set; }
    
    public double PriceDelta { get; private set; }
    public double CurrentPrice { get; private set; }

    public void Update(ReadOnlySpan<MiniAppController.OhlcCandle> candles)
    {
        if (candles.Length == 0) return;

        // Permanent processing of closed candles
        for (int i = 0; i < candles.Length - 1; i++)
        {
            var c = candles[i];
            if (c.Timestamp <= _lastProcessedTime && _lastProcessedTime != default)
                continue;

            // Session reset logic (e.g. gap > 4 hours)
            if (_lastProcessedTime != default && (c.Timestamp - _lastProcessedTime).TotalHours > 4)
            {
                _cumulativeVolumeDelta = 0;
                _shortTermWindow.Clear();
                _shortTermBuyVolume = 0;
                _shortTermSellVolume = 0;
            }

            ProcessCandle(c, isPermanent: true);
            _lastProcessedTime = c.Timestamp;
        }

        // For the current open candle, we calculate the state without permanently committing
        if (candles.Length > 0)
        {
            HasInstitutionalBlockTrade = false;
            
            // Back up the short term values before applying the open candle
            double backupBuy = _shortTermBuyVolume;
            double backupSell = _shortTermSellVolume;

            ProcessCandle(candles[^1], isPermanent: false);

            if (candles.Length >= 5)
            {
                PriceDelta = candles[^1].Close - candles[^5].Close;
            }
            CurrentPrice = candles[^1].Close;
            
            // Restore short term values to not permanently commit the open candle to the rolling sum
            _shortTermBuyVolume = backupBuy;
            _shortTermSellVolume = backupSell;
        }
    }

    private void ProcessCandle(MiniAppController.OhlcCandle c, bool isPermanent)
    {
        double totalVol = c.Volume > 0 ? c.Volume : 1.0;
        
        if (isPermanent)
        {
            _volSum += totalVol;
            _volCount++;
            if (_volCount > 20)
            {
                _volSum -= (_volSum / _volCount);
                _volCount = 20;
            }
            _avgVolume = _volSum / _volCount;
        }

        double noiseThreshold = _avgVolume * 0.60;
        double blockTradeThreshold = _avgVolume * 1.70;

        if (totalVol < noiseThreshold)
            return;

        if (totalVol >= blockTradeThreshold)
            HasInstitutionalBlockTrade = true;

        double range = c.High - c.Low;
        double buyV, sellV;

        if (range > 1e-9)
        {
            double buyRatio = (c.Close - c.Low) / range;
            double sellRatio = (c.High - c.Close) / range;
            buyV = totalVol * buyRatio;
            sellV = totalVol * sellRatio;
        }
        else
        {
            buyV = totalVol * 0.5;
            sellV = totalVol * 0.5;
        }

        if (isPermanent)
        {
            _cumulativeVolumeDelta += (buyV - sellV);

            _shortTermWindow.Enqueue((buyV, sellV));
            _shortTermBuyVolume += buyV;
            _shortTermSellVolume += sellV;

            if (_shortTermWindow.Count > 12)
            {
                var oldest = _shortTermWindow.Dequeue();
                _shortTermBuyVolume -= oldest.buy;
                _shortTermSellVolume -= oldest.sell;
            }
        }
        else
        {
            // For the uncommitted open candle, we just temporarily add its volume to the short-term sum
            // (Note: the calling method will restore the sum immediately after reading properties)
            _shortTermBuyVolume += buyV;
            _shortTermSellVolume += sellV;
        }
    }
}
