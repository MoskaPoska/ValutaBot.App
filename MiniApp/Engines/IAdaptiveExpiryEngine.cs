namespace ValutaBot.MiniApp;

public interface IAdaptiveExpiryEngine
{
    AdaptiveExpiryEngine.OptimalExpiryResult CalculateOptimalExpiry(
        string asset,
        string timeframe,
        double atr,
        double volRatio,
        SmcEngine.SmcAnalysisResult smc,
        bool isSubMinute);
}
