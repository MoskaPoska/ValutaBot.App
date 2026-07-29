namespace ValutaBot.MiniApp;

public interface ITechnicalAnalysisEngine
{
    double ComputeRsi(double[] data, int period = 14);
    double ComputeConnorsRsi(double[] data);
    double ComputeHma(double[] data, int period = 9);
    (double[] Hma, double[] ConnorsRsi) ComputeWalkForwardBatch(double[] data);
    double ComputeEma(double[] data, int period = 9);
    (double macd, double signal) ComputeMacd(double[] data);
    (double adx, double pdi, double mdi) ComputeTrueAdx(MiniAppController.OhlcCandle[] candles, int period = 14);
    double ComputeAtr(MiniAppController.OhlcCandle[] candles, int period = 14);
    double ComputeBollingerZscore(double[] prices, int period = 20);
    
    (double score, double confidence, double rsiVal, double emaVal, double volStrengthVal, double atrVal) ScoreTimeframe(
        double[] prices, double[] volumes, MiniAppController.OhlcCandle[]? candles = null,
        double? adxOverride = null, double? atrOverride = null, bool isForex = false);

    TechnicalAnalysisEngine.GatekeeperResult ValidateMarketGatekeeper(double[] prices, MiniAppController.OhlcCandle[]? candles = null);
    
    double CalculateVolatilityRatio(double[] prices);
}


