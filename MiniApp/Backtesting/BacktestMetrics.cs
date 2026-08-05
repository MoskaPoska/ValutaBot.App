using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using ValutaBot.MiniApp;

namespace ValutaBot.App.MiniApp.Backtesting
{
    /// <summary>
    /// Записывает метрики каждого сигнала в walk-forward симуляции.
    /// </summary>
    public class BacktestMetrics
    {
        public readonly record struct TradeRecord(
            int     CandleIndex,
            DateTime Timestamp,
            string  Direction,
            double  Confidence,
            bool    IsWin,
            string  Regime,
            double  WeightTA,
            double  WeightSMC,
            double  WeightOF,
            double  WeightLGBM,
            double  WeightSkender,
            string  MlDirection,
            double  MlConfidence,
            bool    CalibrationEnabled,
            bool    CooloffActive);

        private readonly List<TradeRecord> _trades = new();

        public int TotalSignals => _trades.Count;

        public void Record(TradeRecord r) => _trades.Add(r);

        // ── Агрегаты ──────────────────────────────────────────────────────────

        public double WinRate()
        {
            int wins = 0;
            foreach (var t in _trades) if (t.IsWin) wins++;
            return _trades.Count == 0 ? 0 : (double)wins / _trades.Count;
        }

        public double RollingWinRate(int window = 50)
        {
            if (_trades.Count < window) return WinRate();
            int wins = 0;
            int start = _trades.Count - window;
            for (int i = start; i < _trades.Count; i++)
                if (_trades[i].IsWin) wins++;
            return (double)wins / window;
        }

        public int CooloffActivations()
        {
            int count = 0;
            foreach (var t in _trades) if (t.CooloffActive) count++;
            return count;
        }

        public Dictionary<string, (int total, int wins)> ByRegime()
        {
            var d = new Dictionary<string, (int total, int wins)>();
            foreach (var t in _trades)
            {
                if (!d.ContainsKey(t.Regime)) d[t.Regime] = (0, 0);
                var (tot, w) = d[t.Regime];
                d[t.Regime] = (tot + 1, t.IsWin ? w + 1 : w);
            }
            return d;
        }

        // ── CSV экспорт ────────────────────────────────────────────────────────

        public async Task SaveCsvAsync(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var sb = new StringBuilder();
            sb.AppendLine("CandleIdx,Timestamp,Direction,Confidence,IsWin,Regime," +
                          "W_TA,W_SMC,W_OF,W_LGBM,W_SKENDER," +
                          "ML_Dir,ML_Conf,CalibOn,CooloffActive,RollingWR50");

            double rollingWr = 0;
            int    windowWins = 0;
            var    windowBuf = new Queue<bool>(50);

            foreach (var t in _trades)
            {
                // Обновляем скользящий WR
                windowBuf.Enqueue(t.IsWin);
                if (t.IsWin) windowWins++;
                if (windowBuf.Count > 50)
                {
                    if (windowBuf.Dequeue()) windowWins--;
                }
                rollingWr = windowBuf.Count > 0 ? (double)windowWins / windowBuf.Count : 0;

                sb.AppendLine($"{t.CandleIndex}," +
                              $"{t.Timestamp:yyyy-MM-dd HH:mm:ss}," +
                              $"{t.Direction}," +
                              $"{t.Confidence:F4}," +
                              $"{(t.IsWin ? 1 : 0)}," +
                              $"{t.Regime}," +
                              $"{t.WeightTA:F4}," +
                              $"{t.WeightSMC:F4}," +
                              $"{t.WeightOF:F4}," +
                              $"{t.WeightLGBM:F4}," +
                              $"{t.WeightSkender:F4}," +
                              $"{t.MlDirection}," +
                              $"{t.MlConfidence:F4}," +
                              $"{(t.CalibrationEnabled ? 1 : 0)}," +
                              $"{(t.CooloffActive ? 1 : 0)}," +
                              $"{rollingWr:F4}");
            }

            await File.WriteAllTextAsync(path, sb.ToString(), Encoding.UTF8);
            Console.WriteLine($"[Metrics] CSV сохранён: {path}");
        }
    }
}
