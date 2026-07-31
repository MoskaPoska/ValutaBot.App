using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;

#r "bin/Debug/net10.0/ValutaBot.App.dll"

try {
    var taEngine = new ValutaBot.App.MiniApp.Engines.TechnicalAnalysisEngine();
    var fetcher = ValutaBot.App.MiniApp.Services.MarketDataFetcher.Instance;
    var wfEngine = new ValutaBot.App.MiniApp.Engines.WalkForwardValidationEngine(taEngine);
    var cmEngine = new ValutaBot.App.MiniApp.Engines.ConfluenceMatrixEngine(fetcher, taEngine);
    var aeEngine = new ValutaBot.App.MiniApp.Engines.AdaptiveExpiryEngine();
    
    var handler = new ValutaBot.MiniApp.CQRS.Handlers.GetMarketAnalysisQueryHandler(
        taEngine, fetcher, wfEngine, cmEngine, aeEngine);
        
    var query = new ValutaBot.MiniApp.CQRS.Queries.GetMarketAnalysisQuery("EURUSD", "m1");
    var res = await handler.Handle(query, default);
    Console.WriteLine("SUCCESS!");
} catch (Exception ex) {
    Console.WriteLine("ERROR:");
    Console.WriteLine(ex.ToString());
}
