using System;

namespace ValutaBot.MiniApp;

public interface IAutoCalibrationEngine
{
    AutoCalibrationEngine.MarketRegime DetectMarketRegime(double adx, double volRatio, double rsi, ReadOnlySpan<double> prices = default);
    double GetCalibratedRegimeWeight(string sourceName, string asset, string timeframe, AutoCalibrationEngine.MarketRegime regime);
    void RecordSourceOutcome(string sourceName, string asset, string timeframe, bool isWin);
    string GetStatsReport(string sourceName, string asset, string timeframe);
}
