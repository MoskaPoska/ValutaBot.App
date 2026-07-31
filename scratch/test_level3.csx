using System;
using System.Net.Http;
using System.Threading.Tasks;

class Program {
    static async Task Main() {
        Console.WriteLine("Starting bot in background...");
        var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo {
            FileName = "dotnet",
            Arguments = "run -c Release",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = @"C:\Users\bural\source\repos\ValutaBot.App"
        });
        
        await Task.Delay(15000); // wait for boot and model load
        
        try {
            using var client = new HttpClient();
            var response = await client.GetAsync("http://localhost:5000/api/signal/BTCUSDT/15m");
            string body = await response.Content.ReadAsStringAsync();
            Console.WriteLine("HTTP " + (int)response.StatusCode);
            Console.WriteLine(body);
        } catch (Exception e) {
            Console.WriteLine(e.Message);
        } finally {
            process.Kill();
        }
    }
}
