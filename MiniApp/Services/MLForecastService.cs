using System;
using System.Linq;
using Skender.Stock.Indicators;

namespace ValutaBot.MiniApp;

/// <summary>
/// Fast, thread-safe time series forecast service using Holt Double Exponential Smoothing.
/// </summary>
public static class MLForecastService
{
    public static (string direction, double confidence, double[] predicted) PredictNextCandles(double[] prices, bool isForex = false, int horizon = 3)
    {
        int n = prices.Length;
        if (n < 10) 
            return ("NEUTRAL", 50, Array.Empty<double>());

        double lastPrice = prices[^1];
        if (lastPrice <= 0 || double.IsNaN(lastPrice) || double.IsInfinity(lastPrice))
            return ("NEUTRAL", 50, Array.Empty<double>());

        // Replace manual Holt smoothing with Skender Double Exponential Moving Average (DEMA)
        int period = Math.Min(14, n / 2);
        if (period < 2) period = 2;
        
        var quotes = prices.Select((p, i) => new Quote { Date = DateTime.UtcNow.AddMinutes(i - n), Close = (decimal)p }).ToList();
        var demaResults = quotes.GetDema(period).ToList();
        
        // Find the last valid DEMA value
        double level = prices[^1];
        double prevLevel = prices.Length > 1 ? prices[^2] : level;
        
        var lastValid = demaResults.LastOrDefault(x => x.Dema != null);
        var prevValid = demaResults.Count > 1 ? demaResults[^2] : lastValid;
        
        if (lastValid != null && lastValid.Dema.HasValue)
        {
            level = (double)lastValid.Dema.Value;
            if (prevValid != null && prevValid.Dema.HasValue)
            {
                prevLevel = (double)prevValid.Dema.Value;
            }
        }
        
        double trend = level - prevLevel;

        var predicted = new double[horizon];
        for (int h = 1; h <= horizon; h++)
        {
            double pred = level + h * trend;
            predicted[h - 1] = (double.IsNaN(pred) || double.IsInfinity(pred)) ? lastPrice : pred;
        }

        double predictedEnd = predicted[^1];
        double change = (predictedEnd - lastPrice) / lastPrice;
        if (double.IsNaN(change) || double.IsInfinity(change))
            change = 0;

        // Volatility estimation for dynamic threshold
        double sumDiff = 0;
        for (int i = 0; i < n - 1; i++)
            sumDiff += Math.Abs(prices[i + 1] - prices[i]);
        
        double volatility = sumDiff / ((n - 1) * lastPrice);
        if (double.IsNaN(volatility) || double.IsInfinity(volatility) || volatility < 1e-9)
            volatility = 0.001;

        double minThreshold = isForex ? 0.00008 : 0.0006;
        double threshold = Math.Max(volatility * 0.15, minThreshold);

        string direction = change > threshold ? "BUY" : change < -threshold ? "PUT" : "NEUTRAL";

        double confidence = direction == "NEUTRAL" 
            ? 50 
            : 58 + Math.Min((Math.Abs(change) / (threshold + 1e-10)) * 15.0, 32.0);

        if (double.IsNaN(confidence) || double.IsInfinity(confidence))
            confidence = 50;

        confidence = Math.Clamp(Math.Round(confidence), 50, 90);

        return (direction, confidence, predicted);
    }
}
