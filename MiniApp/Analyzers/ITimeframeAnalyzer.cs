namespace ValutaBot.MiniApp;

public record TimeframeAnalysisResult(
    string Direction, // "BUY" | "PUT" | "WAIT"
    double Confidence, // 0.0 - 1.0 (e.g. 0.82 = 82%)
    string AnalysisCoreName, // "HFT_MICROSTRUCTURE" | "HYBRID_ENSEMBLE" | "STRUCTURAL_SMC"
    string Reasoning,
    bool IsActionableSignal // True if Confidence >= 0.75 (75%)
);

public interface ITimeframeAnalyzer
{
    Task<TimeframeAnalysisResult> AnalyzeAsync(
        string asset,
        string timeframe,
        double[] prices,
        double[] volumes,
        MiniAppController.OhlcCandle[]? ohlcCandles,
        double adx,
        double atr,
        bool isForex,
        (double[] prices, double[] volumes)? higherTfData
    );
}
