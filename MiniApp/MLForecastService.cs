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

            double lastPrice = prices[^1];
            double predictedEnd = predicted[^1];
            double change = (predictedEnd - lastPrice) / lastPrice;

            string direction = change > 0.001 ? "BUY" : change < -0.001 ? "PUT" : "NEUTRAL";

            // Confidence based on volatility of prediction vs actual
            double volatility = 0;
            for (int i = 0; i < prices.Length - 1; i++)
                volatility += Math.Abs(prices[i + 1] - prices[i]);
            volatility /= (prices.Length - 1) * lastPrice;

            double confidence = Math.Clamp(100 - Math.Abs(change) * 1000 / (volatility * 100 + 0.01), 55, 95);
            confidence = Math.Round(confidence);

            return (direction, confidence, predicted);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ML] SSA failed: {ex.Message}");
            return ("NEUTRAL", 50, Array.Empty<double>());
        }
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
