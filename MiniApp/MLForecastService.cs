using Microsoft.ML;
using Microsoft.ML.TimeSeries;

namespace ValutaBot.MiniApp;

public static class MLForecastService
{
    private static readonly MLContext _ml = new MLContext();

    public static (string direction, double confidence, double[] predicted) PredictNextCandles(double[] prices, int horizon = 3)
    {
        if (prices.Length < 30)
            return ("NEUTRAL", 50, Array.Empty<double>());

        try
        {
            var data = prices.Select((v, i) => new PricePoint { Value = (float)v }).ToArray();
            var dv = _ml.Data.LoadFromEnumerable(data);

            var pipeline = _ml.Forecasting.ForecastBySsa(
                nameof(ForecastResult.Forecast),
                nameof(PricePoint.Value),
                windowSize: Math.Min(prices.Length / 2, 20),
                seriesLength: prices.Length,
                trainSize: prices.Length,
                horizon: horizon);

            var model = pipeline.Fit(dv);

            var forecast = model.Transform(dv);
            var forecastEnumerable = _ml.Data.CreateEnumerable<ForecastResult>(forecast, reuseRowObject: false);
            var forecastResult = forecastEnumerable.FirstOrDefault();

            if (forecastResult?.Forecast == null || forecastResult.Forecast.Length < 2)
                return ("NEUTRAL", 50, Array.Empty<double>());

            var predicted = forecastResult.Forecast.Select(v => (double)v).ToArray();

            // Sanitize predicted values against NaN/Infinity
            for (int i = 0; i < predicted.Length; i++)
            {
                if (double.IsNaN(predicted[i]) || double.IsInfinity(predicted[i]))
                    predicted[i] = prices[^1];
            }

            double lastPrice = prices[^1];
            double predictedEnd = predicted[^1];
            double change = (predictedEnd - lastPrice) / lastPrice;
            if (double.IsNaN(change) || double.IsInfinity(change))
                change = 0;

            string direction = change > 0.001 ? "BUY" : change < -0.001 ? "PUT" : "NEUTRAL";

            // Confidence based on volatility of prediction vs actual
            double volatility = 0;
            for (int i = 0; i < prices.Length - 1; i++)
                volatility += Math.Abs(prices[i + 1] - prices[i]);
            volatility /= (prices.Length - 1) * lastPrice;
            if (double.IsNaN(volatility) || double.IsInfinity(volatility) || volatility < 1e-9)
                volatility = 0.001;

            double confidence = 100 - Math.Abs(change) * 1000 / (volatility * 100 + 0.01);
            if (double.IsNaN(confidence) || double.IsInfinity(confidence))
                confidence = 50;

            confidence = Math.Clamp(confidence, 55, 95);
            confidence = Math.Round(confidence);

            return (direction, confidence, predicted);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ML] SSA failed: {ex.Message}. Falling back to Holt-Linear forecast.");
            return PredictHoltLinear(prices, horizon);
        }
    }

    private static (string direction, double confidence, double[] predicted) PredictHoltLinear(double[] prices, int horizon)
    {
        int n = prices.Length;
        if (n < 10) return ("NEUTRAL", 50, Array.Empty<double>());

        double alpha = 0.3; // Level smoothing coefficient
        double beta = 0.2;  // Trend smoothing coefficient

        double level = prices[0];
        double trend = prices[1] - prices[0];

        for (int i = 1; i < n; i++)
        {
            double lastLevel = level;
            level = alpha * prices[i] + (1 - alpha) * (level + trend);
            trend = beta * (level - lastLevel) + (1 - beta) * trend;
        }

        var predicted = new double[horizon];
        for (int h = 1; h <= horizon; h++)
        {
            predicted[h - 1] = level + h * trend;
        }

        double lastPrice = prices[^1];
        double predictedEnd = predicted[^1];
        double change = (predictedEnd - lastPrice) / lastPrice;

        string direction = change > 0.0015 ? "BUY" : change < -0.0015 ? "PUT" : "NEUTRAL";

        // Confidence estimation
        double mean = prices.Average();
        double variance = prices.Sum(p => Math.Pow(p - mean, 2)) / n;
        double std = Math.Sqrt(variance);
        
        double confidence = 50;
        if (std > 1e-9)
        {
            confidence = 100 - (Math.Abs(predictedEnd - lastPrice) / std) * 12;
        }
        confidence = Math.Clamp(confidence, 55, 90);
        confidence = Math.Round(confidence);

        return (direction, confidence, predicted);
    }

    private class PricePoint
    {
        public float Value { get; set; }
    }

    private class ForecastResult
    {
        public float[] Forecast { get; set; } = Array.Empty<float>();
    }
}
