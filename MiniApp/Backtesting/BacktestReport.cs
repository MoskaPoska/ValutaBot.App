using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace ValutaBot.App.MiniApp.Backtesting
{
    public static class BacktestReport
    {
        public static async Task SaveJsonAsync(
            BacktestMetrics  calibOn,
            BacktestMetrics  calibOff,
            string           timeframe,
            int              totalCandles,
            string           path)
        {
            var report = new
            {
                GeneratedAt       = DateTime.UtcNow,
                Timeframe         = timeframe,
                TotalCandlesUsed  = totalCandles,

                CalibrationON = new
                {
                    TotalSignals        = calibOn.TotalSignals,
                    WinRate             = Math.Round(calibOn.WinRate() * 100, 2),
                    RollingWinRate50    = Math.Round(calibOn.RollingWinRate(50) * 100, 2),
                    CooloffActivations  = calibOn.CooloffActivations(),
                    ByRegime            = calibOn.ByRegime()
                },
                CalibrationOFF = new
                {
                    TotalSignals        = calibOff.TotalSignals,
                    WinRate             = Math.Round(calibOff.WinRate() * 100, 2),
                    RollingWinRate50    = Math.Round(calibOff.RollingWinRate(50) * 100, 2),
                    CooloffActivations  = calibOff.CooloffActivations(),
                    ByRegime            = calibOff.ByRegime()
                },
                CalibrationDelta = new
                {
                    WinRateDiff   = Math.Round((calibOn.WinRate() - calibOff.WinRate()) * 100, 2),
                    SelfLearningEffective = calibOn.WinRate() > calibOff.WinRate()
                }
            };

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var json = JsonSerializer.Serialize(report,
                new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(path, json);
            Console.WriteLine($"[Report] JSON сохранён: {path}");
        }

        public static void PrintSummary(
            BacktestMetrics calibOn,
            BacktestMetrics calibOff,
            string timeframe,
            int totalCandles)
        {
            Console.WriteLine();
            Console.WriteLine("═══════════════════════════════════════════════════════");
            Console.WriteLine($"  BACKTEST РЕЗУЛЬТАТ | {timeframe.ToUpper()} | {totalCandles} свечей");
            Console.WriteLine("═══════════════════════════════════════════════════════");
            Console.WriteLine();
            Console.WriteLine("  ┌─ AutoCalibration ON ─────────────────────────────┐");
            Console.WriteLine($"  │  Сигналов:      {calibOn.TotalSignals}");
            Console.WriteLine($"  │  Win Rate:      {calibOn.WinRate() * 100:F2}%");
            Console.WriteLine($"  │  Rolling WR50:  {calibOn.RollingWinRate(50) * 100:F2}%");
            Console.WriteLine($"  │  Cooloff акт.:  {calibOn.CooloffActivations()}");
            Console.WriteLine("  └──────────────────────────────────────────────────┘");
            Console.WriteLine();
            Console.WriteLine("  ┌─ AutoCalibration OFF (базовая линия) ────────────┐");
            Console.WriteLine($"  │  Сигналов:      {calibOff.TotalSignals}");
            Console.WriteLine($"  │  Win Rate:      {calibOff.WinRate() * 100:F2}%");
            Console.WriteLine($"  │  Rolling WR50:  {calibOff.RollingWinRate(50) * 100:F2}%");
            Console.WriteLine($"  │  Cooloff акт.:  {calibOff.CooloffActivations()}");
            Console.WriteLine("  └──────────────────────────────────────────────────┘");
            Console.WriteLine();

            double delta = (calibOn.WinRate() - calibOff.WinRate()) * 100;
            string verdict = delta > 0
                ? $"✅ Самообучение ЭФФЕКТИВНО: +{delta:F2}% WR"
                : $"⚠️  Самообучение не дало преимущества: {delta:F2}% WR";
            Console.WriteLine($"  {verdict}");
            Console.WriteLine();
            Console.WriteLine("  По режимам (CalibON):");
            foreach (var kv in calibOn.ByRegime())
            {
                double wr = kv.Value.total > 0 ? (double)kv.Value.wins / kv.Value.total * 100 : 0;
                Console.WriteLine($"    {kv.Key,-30} {kv.Value.total,5} сделок | WR {wr:F1}%");
            }
            Console.WriteLine("═══════════════════════════════════════════════════════");
        }
    }
}
