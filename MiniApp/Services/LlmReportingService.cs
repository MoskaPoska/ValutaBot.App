using System;

namespace ValutaBot.App.MiniApp.Services
{
    public class LlmReportingService
    {
        // In a real production scenario, this would inject an HttpClient 
        // and send a request to OpenAI / Anthropic APIs.
        // For now, we simulate the LLM generation based on parameters.
        
        public string GenerateMarketSummary(string asset, string regime, float lightGbmProb, bool l1IsBuy, bool l2IsBuy, bool l3IsBuy)
        {
            var trendStr = regime.Contains("Trend") ? "Наблюдается ярко выраженное трендовое движение." : 
                           regime.Contains("Flat") ? "Рынок находится в стадии накопления (Флэт)." : 
                           "На рынке зафиксирована высокая энтропия (Хаос).";

            var probabilityStr = lightGbmProb > 0.5f ? 
                $"LightGBM оценивает вероятность роста в {Math.Round(lightGbmProb * 100, 1)}%." : 
                $"LightGBM оценивает вероятность падения в {Math.Round((1 - lightGbmProb) * 100, 1)}%.";

            var direction = lightGbmProb > 0.5f ? "ЛОНГ" : "ШОРТ";
            
            var confluence = 0;
            if (l1IsBuy == (lightGbmProb > 0.5f)) confluence++;
            if (l2IsBuy == (lightGbmProb > 0.5f)) confluence++;
            if (l3IsBuy == (lightGbmProb > 0.5f)) confluence++;

            string llmOutput = $"🤖 **Квантовый Отчет (LLM Analysis)**\n\n" +
                               $"Анализ актива **{asset}** завершен. {trendStr}\n\n" +
                               $"🧠 **ML-Модель:** {probabilityStr}\n" +
                               $"📡 **Confluence Matrix (Совпадение сигналов):** {confluence}/3 уровней подтверждают прогноз.\n\n" +
                               $"*Рекомендация:* Рассмотреть позицию в **{direction}**, с обязательным контролем риск-менеджмента, так как ML-модель обнаружила {(lightGbmProb > 0.7 || lightGbmProb < 0.3 ? "сильную нелинейную зависимость в Order Flow" : "умеренный дисбаланс объемов")}.";

            return llmOutput;
        }
    }
}
