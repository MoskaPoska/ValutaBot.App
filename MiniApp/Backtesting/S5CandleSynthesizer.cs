using System;
using ValutaBot.MiniApp;

namespace ValutaBot.App.MiniApp.Backtesting
{
    /// <summary>
    /// Синтезирует S5-свечи из M1 методом OHLC-декомпозиции.
    /// Каждая M1-свеча → 12 S5-свечей с реалистичным внутренним движением.
    /// ВАЖНО: Результат — синтетика, пригодна для stress-test движков,
    /// но не отражает реальную рыночную структуру S5.
    /// </summary>
    public static class S5CandleSynthesizer
    {
        private const int SubCandlesPerMinute = 12; // 60s / 5s = 12
        private static readonly Random _rng = new(42); // seed для воспроизводимости

        public static MiniAppController.OhlcCandle[] SynthesizeFromM1(
            MiniAppController.OhlcCandle[] m1Candles)
        {
            var result = new MiniAppController.OhlcCandle[m1Candles.Length * SubCandlesPerMinute];
            int idx = 0;

            foreach (var m1 in m1Candles)
            {
                double open  = m1.Open;
                double close = m1.Close;
                double high  = m1.High;
                double low   = m1.Low;
                double range = high - low;
                double pip   = range > 0 ? range * 0.01 : 0.00001;

                // Строим путь цены: Open → (возможный High/Low в середине) → Close
                // Пик/долина в районе 4-7 свечи (середина минуты)
                int peakAt = 4 + _rng.Next(4);
                bool goHighFirst = close >= open
                    ? (_rng.NextDouble() > 0.3)  // в тренде вверх — High раньше
                    : (_rng.NextDouble() > 0.7);  // в тренде вниз — High может быть в начале

                for (int i = 0; i < SubCandlesPerMinute; i++)
                {
                    double t      = (double)i / (SubCandlesPerMinute - 1); // 0..1
                    double tNext  = (double)(i + 1) / (SubCandlesPerMinute - 1);

                    // Линейная интерполяция базового пути
                    double basePrice     = open + (close - open) * t;
                    double basePriceNext = open + (close - open) * Math.Min(tNext, 1.0);

                    // Добавляем реалистичный профиль High/Low
                    double peakFactor = Math.Sin(Math.PI * i / (SubCandlesPerMinute - 1));
                    double excursion  = range * 0.5 * peakFactor;

                    double subHigh, subLow;
                    if (goHighFirst)
                    {
                        subHigh = Math.Max(basePrice, basePriceNext) + excursion + _rng.NextDouble() * pip;
                        subLow  = Math.Min(basePrice, basePriceNext) - pip * _rng.NextDouble() * 0.3;
                    }
                    else
                    {
                        subHigh = Math.Max(basePrice, basePriceNext) + pip * _rng.NextDouble() * 0.3;
                        subLow  = Math.Min(basePrice, basePriceNext) - excursion - _rng.NextDouble() * pip;
                    }

                    // Гарантируем что S5 high/low не выходят за M1 high/low
                    subHigh = Math.Min(subHigh, high);
                    subLow  = Math.Max(subLow,  low);
                    subHigh = Math.Max(subHigh, Math.Max(basePrice, basePriceNext));
                    subLow  = Math.Min(subLow,  Math.Min(basePrice, basePriceNext));

                    DateTime subDt = m1.Timestamp.AddSeconds(i * 5);
                    result[idx++] = new MiniAppController.OhlcCandle(
                        Open:   basePrice,
                        High:   subHigh,
                        Low:    subLow,
                        Close:  i == SubCandlesPerMinute - 1 ? close : basePriceNext,
                        Volume: 0,
                        Timestamp:   subDt);
                }
            }

            return result;
        }
    }
}
