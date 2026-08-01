using System;
using System.Collections.Generic;
using Microsoft.ML;

namespace ValutaBot.App.MiniApp.Engines.ML
{


    public class EnsemblePrediction
    {
        public bool ConsensusPrediction { get; set; }
        public float AverageProbability { get; set; }
        public int VotesForUp { get; set; }
        public int VotesForDown { get; set; }
        public float FinalScoreMultiplier { get; set; }
        public Dictionary<string, float> ModelProbabilities { get; set; } = new();
    }

    public class EnsembleMlEngine
    {
        private readonly MLContext _mlContext;
        private ITransformer? _lightGbmModel;
        private ITransformer? _fastTreeModel;
        private ITransformer? _fastForestModel;

        public EnsembleMlEngine()
        {
            _mlContext = new MLContext(seed: 42);
        }

        public void TrainModels(IEnumerable<TradeFeatureData> historicalData)
        {
            var dataView = _mlContext.Data.LoadFromEnumerable(historicalData);

            // Create common data prep pipeline
            var dataPrep = _mlContext.Transforms.Concatenate("Features", 
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
                );

            // 1. Train LightGBM
            var lgbmPipeline = dataPrep.Append(_mlContext.BinaryClassification.Trainers.LightGbm(
                labelColumnName: "Label", featureColumnName: "Features",
                numberOfLeaves: 10, minimumExampleCountPerLeaf: 2, learningRate: 0.1));
            _lightGbmModel = lgbmPipeline.Fit(dataView);

            // 2. Train FastTree (XGBoost analog)
            var fastTreePipeline = dataPrep.Append(_mlContext.BinaryClassification.Trainers.FastTree(
                labelColumnName: "Label", featureColumnName: "Features",
                numberOfLeaves: 10, minimumExampleCountPerLeaf: 2, learningRate: 0.1));
            _fastTreeModel = fastTreePipeline.Fit(dataView);

            // 3. Train FastForest (Random Forest analog)
            var fastForestPipeline = dataPrep.Append(_mlContext.BinaryClassification.Trainers.FastForest(
                labelColumnName: "Label", featureColumnName: "Features",
                numberOfLeaves: 10, minimumExampleCountPerLeaf: 2));
            _fastForestModel = fastForestPipeline.Fit(dataView);
        }

        public EnsemblePrediction PredictEnsemble(TradeFeatureData currentMarketData)
        {
            var result = new EnsemblePrediction();
            if (_lightGbmModel == null || _fastTreeModel == null || _fastForestModel == null)
            {
                result.AverageProbability = 0.5f;
                return result;
            }

            // Create prediction engines
            var lgbmEngine = _mlContext.Model.CreatePredictionEngine<TradeFeatureData, TradePrediction>(_lightGbmModel);
            var treeEngine = _mlContext.Model.CreatePredictionEngine<TradeFeatureData, TradePrediction>(_fastTreeModel);
            var forestEngine = _mlContext.Model.CreatePredictionEngine<TradeFeatureData, TradePrediction>(_fastForestModel);

            // Predict
            var pLgbm = lgbmEngine.Predict(currentMarketData);
            var pTree = treeEngine.Predict(currentMarketData);
            var pForest = forestEngine.Predict(currentMarketData);

            result.ModelProbabilities["LightGBM"] = pLgbm.Probability;
            result.ModelProbabilities["FastTree"] = pTree.Probability;
            result.ModelProbabilities["FastForest"] = pForest.Probability;

            // Voting Logic
            if (pLgbm.Probability > 0.5f) result.VotesForUp++; else result.VotesForDown++;
            if (pTree.Probability > 0.5f) result.VotesForUp++; else result.VotesForDown++;
            if (pForest.Probability > 0.5f) result.VotesForUp++; else result.VotesForDown++;

            result.AverageProbability = (pLgbm.Probability + pTree.Probability + pForest.Probability) / 3.0f;
            result.ConsensusPrediction = result.VotesForUp >= 2;

            // Compute score multiplier.
            // If all 3 agree strongly (>60%), high multiplier.
            if (result.VotesForUp == 3 && result.AverageProbability > 0.60f)
                result.FinalScoreMultiplier = 0.35f;
            else if (result.VotesForDown == 3 && result.AverageProbability < 0.40f)
                result.FinalScoreMultiplier = -0.35f;
            else if (result.VotesForUp >= 2 && result.AverageProbability > 0.55f)
                result.FinalScoreMultiplier = 0.15f;
            else if (result.VotesForDown >= 2 && result.AverageProbability < 0.45f)
                result.FinalScoreMultiplier = -0.15f;
            else
                result.FinalScoreMultiplier = 0f; // Neutral / mixed signals

            return result;
        }
    }
}
