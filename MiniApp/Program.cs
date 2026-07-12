using System.Text.Json;

namespace ValutaBot.MiniApp;

internal static class Program
{
    public static void Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--test")
        {
            RunLocalTests().GetAwaiter().GetResult();
            return;
        }

        try { Console.Title = "TradeBE Smart Terminal Core"; } catch { /* not a TTY (Docker/Linux) */ }

        var port = int.TryParse(Environment.GetEnvironmentVariable("PORT"), out var p) ? p : 5000;

        while (true)
        {
            try
            {
                MiniAppController.Start(args, port);
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[!] Crash: {ex.Message}");
                Console.WriteLine("[+] Auto-restart in 3s... (Ctrl+C to exit)");
                Thread.Sleep(3000);
            }
        }
    }

    private static async System.Threading.Tasks.Task RunLocalTests()
    {
        Console.WriteLine("==================================================");
        Console.WriteLine("        RUNNING LOCAL INTEGRATION TESTS           ");
        Console.WriteLine("==================================================");

        try
        {
            var options = new JsonSerializerOptions
            {
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                WriteIndented = true
            };

            Console.WriteLine("\n[1] Testing sub-minute AI bypass on S5 timeframe...");
            var s5Result = await MiniAppController.ExecuteBinanceAnalysis("BTC/USDT", "s5");
            string s5Json = JsonSerializer.Serialize(s5Result, options);
            Console.WriteLine(s5Json);

            if (s5Json.Contains("Математический анализ") && s5Json.Contains("S5"))
            {
                Console.WriteLine("=> [PASS] S5 test passed successfully!");
            }
            else
            {
                Console.WriteLine("=> [FAIL] S5 test failed.");
            }

            Console.WriteLine("\n[2] Testing regular timeframe M1 analysis...");
            var m1Result = await MiniAppController.ExecuteBinanceAnalysis("BTC/USDT", "m1");
            string m1Json = JsonSerializer.Serialize(m1Result, options);
            Console.WriteLine(m1Json);

            if (m1Json.Contains("M1"))
            {
                Console.WriteLine("=> [PASS] M1 test passed successfully!");
            }
            else
            {
                Console.WriteLine("=> [FAIL] M1 test failed.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"=> [ERROR] Test run threw an exception: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
        }

        Console.WriteLine("==================================================");
    }
}
