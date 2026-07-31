using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using ValutaBot.MiniApp;

// Create a DI container to simulate MiniAppController's HTTP factory
var services = new ServiceCollection();
services.AddHttpClient("Binance").AddStandardResilienceHandler();
services.AddHttpClient("TwelveDataService").AddStandardResilienceHandler();
var provider = services.BuildServiceProvider();
var httpFactory = provider.GetRequiredService<System.Net.Http.IHttpClientFactory>();

// Reflection trick to set MiniAppController.HttpFactory
var prop = typeof(MiniAppController).GetProperty("HttpFactory");
prop.SetValue(null, httpFactory);

Console.WriteLine("[TEST] HttpFactory injected into MiniAppController.");

// 1. Test MarketDataFetcher
try {
    Console.WriteLine("[TEST] Fetching BTCUSDT from Binance...");
    var price = await MarketDataFetcher.Instance.FetchHistoricalPriceAsync("BTCUSDT", "1m");
    Console.WriteLine($"[TEST] Binance BTCUSDT: {price}");
} catch (Exception ex) {
    Console.WriteLine($"[TEST] Binance ERROR: {ex.Message}");
}

// 2. Test BotDatabase
try {
    Console.WriteLine("[TEST] Connecting to Database...");
    var connStr = Environment.GetEnvironmentVariable("DATABASE_URL");
    if (!string.IsNullOrEmpty(connStr)) {
        using var conn = BotDatabase.GetConnection();
        await conn.OpenAsync();
        Console.WriteLine("[TEST] Database connected successfully via NpgsqlDataSource!");
    } else {
        Console.WriteLine("[TEST] No DATABASE_URL found. Skipping DB test.");
    }
} catch (Exception ex) {
    Console.WriteLine($"[TEST] Database ERROR: {ex.Message}");
}

Console.WriteLine("[TEST] Finished Level 2 Verification.");
