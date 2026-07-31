using System;
using System.Threading.Tasks;

class Program {
    static async Task Main() {
        try {
            var taEngine = new ValutaBot.MiniApp.TechnicalAnalysisEngine();
            var fetcher = ValutaBot.MiniApp.MarketDataFetcher.Instance;
            var wfEngine = new ValutaBot.MiniApp.WalkForwardValidationEngine(taEngine);
            var cmEngine = new ValutaBot.MiniApp.ConfluenceMatrixEngine(fetcher, taEngine);
            var aeEngine = new ValutaBot.MiniApp.AdaptiveExpiryEngine();
            
            var handler = new ValutaBot.MiniApp.CQRS.Handlers.GetMarketAnalysisQueryHandler(
                taEngine, fetcher, wfEngine, cmEngine, aeEngine);
                
            var query = new ValutaBot.MiniApp.CQRS.Queries.GetMarketAnalysisQuery("EURUSD", "m1");
            var res = await handler.Handle(query, default);
            Console.WriteLine("SUCCESS!");
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(res));
        } catch (Exception ex) {
            Console.WriteLine("ERROR:");
            Console.WriteLine(ex.ToString());
        }
    }
}
