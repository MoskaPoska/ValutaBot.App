using System;
using System.Collections.Generic;
using Microsoft.ML;

namespace ValutaBot.App.MiniApp.Engines.ML
{
    public class LightGbmEngine
    {
        private readonly MLContext _mlContext;
        private ITransformer? _model;

        public LightGbmEngine()
        {
            _mlContext = new MLContext(seed: 42);
        }

        public void TrainModel(IEnumerable<TradeFeatureData> historicalData)
        {
            // 1. Load data
            var dataView = _mlContext.Data.LoadFromEnumerable(historicalData);

            // 2. Build Pipeline
            var pipeline = _mlContext.Transforms.Concatenate("Features", 
                    nameof(TradeFeatureData.Open), 
                    nameof(TradeFeatureData.High), 
                    nameof(TradeFeatureData.Low), 
                    nameof(TradeFeatureData.Close), 
                    nameof(TradeFeatureData.Volume),
                    nameof(TradeFeatureData.Rsi),
                    nameof(TradeFeatureData.Macd),
                    nameof(TradeFeatureData.BollingerUpper),
                    nameof(TradeFeatureData.BollingerLower),
                    nameof(TradeFeatureData.ClusterDelta),
                    nameof(TradeFeatureData.ImbalanceSize)
                )
                .Append(_mlContext.BinaryClassification.Trainers.LightGbm(
                    labelColumnName: "Label", 
                    featureColumnName: "Features"));

            // 3. Train
            _model = pipeline.Fit(dataView);
        }

        public TradePrediction Predict(TradeFeatureData currentMarketData)
        {
            if (_model == null)
            {
                // If model not trained, return neutral 50%
                return new TradePrediction { Prediction = false, Probability = 0.5f, Score = 0 };
            }

            // Create prediction engine
            var predictionEngine = _mlContext.Model.CreatePredictionEngine<TradeFeatureData, TradePrediction>(_model);
            
            // Predict
            return predictionEngine.Predict(currentMarketData);
        }

        public (float probability, string recommendation) AnalyzeProbability(TradePrediction prediction)
        {
            // Probability is calibrated between 0 and 1.
            // LightGBM outputs the probability of the Positive class (Label = true).
            float prob = prediction.Probability;
            
            if (prob > 0.65f) return (prob, "Strong Buy (LightGBM Edge)");
            if (prob < 0.35f) return (prob, "Strong Sell (LightGBM Edge)");
            
            return (prob, "Neutral / Wait");
        }
    }
}
