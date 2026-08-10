using System;
using ValutaBot.MiniApp;

namespace ValutaBot.App.MiniApp.Services
{
    public class LlmReportingService
    {
        public string GenerateMarketSummary(string asset, string regime, MLPythonService.MLPythonPrediction? mlPrediction, bool l1IsBuy, bool l2IsBuy, bool l3IsBuy)
        {
            var trendStr = regime.Contains("Trend") ? "Наблюдается ярко выраженное трендовое движение." : 
                           regime.Contains("Flat") ? "Рынок находится в стадии накопления (Флэт)." : 
                           "На рынке зафиксирована высокая энтропия (Хаос).";

            var direction = mlPrediction != null && mlPrediction.Direction == "BUY" ? "ВВЕРХ 🟢" : 
                            mlPrediction != null && mlPrediction.Direction == "PUT" ? "ВНИЗ 🔴" : "НЕЙТРАЛЬНО ⚪";
            
            float confidence = mlPrediction != null ? (float)mlPrediction.Confidence : 0.5f;
            string modelVersion = mlPrediction != null ? mlPrediction.ModelVersion : "N/A";

            var confluence = 0;
            if (mlPrediction != null && mlPrediction.Direction != "NEUTRAL")
            {
                bool isBuy = mlPrediction.Direction == "BUY";
                if (l1IsBuy == isBuy) confluence++;
                if (l2IsBuy == isBuy) confluence++;
                if (l3IsBuy == isBuy) confluence++;
            }

            string llmOutput = $"🤖 **Автоматизированный Анализ**\n\n" +
                               $"Анализ актива **{asset}** завершен. {trendStr}\n\n";

            if (mlPrediction != null && mlPrediction.Direction != "NEUTRAL")
            {
                llmOutput += $"🧠 **Ансамбль ML ({modelVersion}):**\n" +
                             $"- Вектор: {direction}\n" +
                             $"- Уверенность: {Math.Round(confidence * 100, 1)}%\n\n" +
                             $"📡 **Тех. Совпадение (Confluence):** {confluence}/3 уровней подтверждают ML-прогноз.\n\n" +
                             $"*Рекомендация:* Рассмотреть позицию {direction}, с обязательным контролем риск-менеджмента.";
            }
            else
            {
                llmOutput += $"🧠 **Ансамбль ML:** Оффлайн или низкая уверенность. Используется чистая математика (Уровни 1-3).\n\n" +
                             $"*Рекомендация:* Опирайтесь на математический скоринг и Риск-Менеджмент.";
            }

            return llmOutput;
        }
    }
}
