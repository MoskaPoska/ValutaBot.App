namespace ValutaBot.MiniApp;

public interface IWalkForwardValidationEngine
{
    WalkForwardValidationEngine.WalkForwardResult ValidateWalkForward(
        string asset,
        string timeframe,
        double[] prices,
        bool isNewsActive = false);

    void RecordTradeOutcome(string asset, string timeframe, bool isWin);
}
