namespace ValutaBot.MiniApp;

public class AutoAnalysisService : BackgroundService
{
    private static readonly string[] Assets =
    [
        "EUR/USD OTC", "GBP/USD OTC", "USD/JPY OTC", "EUR/JPY OTC", "GBP/JPY OTC",
        "AUD/USD OTC", "USD/CAD OTC", "USD/CHF OTC", "NZD/USD OTC", "EUR/GBP OTC",
        "AUD/CAD OTC", "CAD/CHF OTC", "EUR/CHF OTC", "EUR/NZD OTC", "NZD/JPY OTC",
        "USD/BRL OTC", "USD/IDR OTC", "USD/PKR OTC", "USD/DZD OTC", "NGN/USD OTC",
        "LBP/USD OTC", "TND/USD OTC", "JOD/CNY OTC", "OMR/CNY OTC", "SAR/CNY OTC",
        "GOLD OTC", "SILVER OTC", "BRENT OTC", "OIL OTC",
        "BTC/USDT OTC", "ETH/USDT OTC", "SOL/USDT OTC",
        "AAPL OTC", "TSLA OTC", "AMZN OTC", "GOOGL OTC", "MSFT OTC"
    ];

    private static readonly string[] Timeframes = ["m1", "h1"];
    private static readonly Dictionary<string, (string direction, int probability, string tf)> _prev = new();
    private static readonly Random _jitter = new();
    private int _cycle;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        string disableVal = Environment.GetEnvironmentVariable("DISABLE_AUTO_ANALYSIS") ?? "false";
        if (disableVal.Equals("true", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("[Auto] AutoAnalysisService is disabled via environment variable to preserve TwelveData API credits.");
            return;
        }

        Console.WriteLine("[Auto] Service started — will analyze every ~15 min (or custom interval)");
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _cycle++;
                string tf = Timeframes[_cycle % 2];

                // ─── 3 major pairs: analyze on BOTH timeframes every cycle ───
                string[] majors = ["EUR/USD OTC", "GBP/USD OTC", "AUD/USD OTC"];
                Console.WriteLine($"[Auto] Cycle #{_cycle} — analyzing {Assets.Length} assets on {tf} + majors on m1/h1");

                foreach (string majorTf in Timeframes)
                {
                    foreach (var asset in majors)
                    {
                        if (stoppingToken.IsCancellationRequested) break;
                        await AnalyzeAsset(asset, majorTf, stoppingToken);
                    }
                }

                // ─── Other assets: alternating TF ───
                foreach (var asset in Assets)
                {
                    if (stoppingToken.IsCancellationRequested) break;
                    if (majors.Contains(asset)) continue;
                    await AnalyzeAsset(asset, tf, stoppingToken);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Auto] Cycle failed: {ex.Message}");
            }

            int delayMin = 12 + _jitter.Next(6);
            string intervalStr = Environment.GetEnvironmentVariable("AUTO_ANALYSIS_INTERVAL_MINUTES") ?? "";
            if (int.TryParse(intervalStr, out int customInterval) && customInterval > 0)
            {
                delayMin = customInterval;
            }

            Console.WriteLine($"[Auto] Next cycle in ~{delayMin} min");
            await Task.Delay(TimeSpan.FromMinutes(delayMin), stoppingToken);
        }
    }

    private async Task AnalyzeAsset(string asset, string tf, CancellationToken ct)
    {
        try
        {
            dynamic result = await MiniAppController.ExecuteBinanceAnalysis(asset, tf);
            string dir = result.direction;
            int prob = result.probability;
            string key = $"{asset}_{tf}";

            if (_prev.TryGetValue(key, out var prev) && prev.direction != dir)
            {
                long chatId = TelegramNotifier.GetDefaultChatId();
                if (chatId > 0)
                {
                    await TelegramNotifier.SendAlert(chatId, "🚀 СМЕНА СИГНАЛА", $"{asset} ({tf})\n{prev.direction} ({prev.probability}%) → <b>{dir}</b> ({prob}%)");
                    Console.WriteLine($"[Auto] ALERT {asset} {prev.direction}→{dir}");
                }
            }

            _prev[key] = (dir, prob, tf);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Auto] Skip {asset}: {ex.Message}");
        }

        await Task.Delay(500 + _jitter.Next(200), ct);
    }
}
