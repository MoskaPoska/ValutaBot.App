namespace ValutaBot.MiniApp;

public interface IAnalysisOrchestrator
{
    Task<object> ExecuteBinanceAnalysis(string asset, string timeframe);
}
