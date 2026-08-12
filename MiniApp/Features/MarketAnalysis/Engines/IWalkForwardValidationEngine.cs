namespace ValutaBot.MiniApp;

public interface IWalkForwardValidationEngine
{
    WalkForwardValidationEngine.WalkForwardResult ValidateWalkForward(
        string asset,
        string timeframe);

    void RecordTradeOutcome(string asset, string timeframe, bool isWin);
}
