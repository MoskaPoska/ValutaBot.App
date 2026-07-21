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

        // Holt's Double Exponential Smoothing for trend and level
        double alpha = 0.3; // Level smoothing coefficient
        double beta  = 0.2; // Trend smoothing coefficient

        double level = prices[0];
        double trend = prices[1] - prices[0];

        for (int i = 1; i < n; i++)
        {
            double p = prices[i];
            if (double.IsNaN(p) || double.IsInfinity(p)) continue;

            double lastLevel = level;
            level = alpha * p + (1 - alpha) * (level + trend);
            trend = beta * (level - lastLevel) + (1 - beta) * trend;
        }

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

        double minThreshold = isForex ? 0.00025 : 0.0020;
        double threshold = Math.Max(volatility * 0.30, minThreshold);

        string direction = change > threshold ? "BUY" : change < -threshold ? "PUT" : "NEUTRAL";

        double confidence = direction == "NEUTRAL" ? 50 : 58 + Math.Min((Math.Abs(change) / (threshold + 1e-10)) * 12.0, 32.0);
        if (double.IsNaN(confidence) || double.IsInfinity(confidence))
            confidence = 50;

        confidence = Math.Clamp(Math.Round(confidence), 50, 90);

        return (direction, confidence, predicted);
    }
}
