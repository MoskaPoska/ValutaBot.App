using System.Threading.Tasks;

namespace ValutaBot.MiniApp.Features.MarketAnalysis;

public interface IMarketAnalysisOrchestrator
{
    Task<object> ExecuteAnalysisAsync(string asset, string timeframe, ValutaBot.App.MiniApp.Data.Repositories.UserSettings? userSettings = null);
}
