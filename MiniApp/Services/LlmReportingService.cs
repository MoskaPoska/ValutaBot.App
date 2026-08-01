using System;

namespace ValutaBot.App.MiniApp.Services
{
    public class LlmReportingService
    {
        public string GenerateMarketSummary(string asset, string regime, Engines.ML.EnsemblePrediction mlPrediction, bool l1IsBuy, bool l2IsBuy, bool l3IsBuy)
        {
            var trendStr = regime.Contains("Trend") ? "Наблюдается ярко выраженное трендовое движение." : 
                           regime.Contains("Flat") ? "Рынок находится в стадии накопления (Флэт)." : 
                           "На рынке зафиксирована высокая энтропия (Хаос).";

            var direction = mlPrediction.ConsensusPrediction ? "ВВЕРХ 🟢" : "ВНИЗ 🔴";
            float lgbmProb = mlPrediction.ModelProbabilities.GetValueOrDefault("LightGBM", 0.5f);
            float treeProb = mlPrediction.ModelProbabilities.GetValueOrDefault("FastTree", 0.5f);
            float forestProb = mlPrediction.ModelProbabilities.GetValueOrDefault("FastForest", 0.5f);

            var confluence = 0;
            if (l1IsBuy == mlPrediction.ConsensusPrediction) confluence++;
            if (l2IsBuy == mlPrediction.ConsensusPrediction) confluence++;
            if (l3IsBuy == mlPrediction.ConsensusPrediction) confluence++;

            string llmOutput = $"🤖 **Нейро-Анализ (Ансамбль ML)**\n\n" +
                               $"Анализ актива **{asset}** завершен. {trendStr}\n\n" +
                               $"🧠 **Голосование моделей:**\n" +
                               $"- LightGBM: {Math.Round(lgbmProb * 100, 1)}% ({(lgbmProb > 0.5f ? "ВВЕРХ" : "ВНИЗ")})\n" +
                               $"- FastTree: {Math.Round(treeProb * 100, 1)}% ({(treeProb > 0.5f ? "ВВЕРХ" : "ВНИЗ")})\n" +
                               $"- FastForest: {Math.Round(forestProb * 100, 1)}% ({(forestProb > 0.5f ? "ВВЕРХ" : "ВНИЗ")})\n" +
                               $"\n✅ **Консенсус:** {direction} (Уверенность: {Math.Round(mlPrediction.AverageProbability * 100, 1)}%)\n\n" +
                               $"📡 **Тех. Совпадение (Confluence):** {confluence}/3 уровней подтверждают ML-прогноз.\n\n" +
                               $"*Рекомендация:* Рассмотреть позицию {direction}, с обязательным контролем риск-менеджмента.";

            return llmOutput;
        }
    }
}
