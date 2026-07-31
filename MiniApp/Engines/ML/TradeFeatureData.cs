using Microsoft.ML.Data;

namespace ValutaBot.App.MiniApp.Engines.ML
{
    public class TradeFeatureData
    {
        [LoadColumn(0)] public float Open { get; set; }
        [LoadColumn(1)] public float High { get; set; }
        [LoadColumn(2)] public float Low { get; set; }
        [LoadColumn(3)] public float Close { get; set; }
        [LoadColumn(4)] public float Volume { get; set; }

        // Tech indicators
        [LoadColumn(5)] public float Rsi { get; set; }
        [LoadColumn(6)] public float Macd { get; set; }
        [LoadColumn(7)] public float BollingerUpper { get; set; }
        [LoadColumn(8)] public float BollingerLower { get; set; }

        // Order Flow & SMC features
        [LoadColumn(9)] public float ClusterDelta { get; set; }
        [LoadColumn(10)] public float ImbalanceSize { get; set; }

        // Label: True if price goes up in the next N periods
        [LoadColumn(11), ColumnName("Label")] public bool IsUp { get; set; }
    }

    public class TradePrediction
    {
        [ColumnName("PredictedLabel")]
        public bool Prediction { get; set; }

        [ColumnName("Probability")]
        public float Probability { get; set; }

        [ColumnName("Score")]
        public float Score { get; set; }
    }
}
