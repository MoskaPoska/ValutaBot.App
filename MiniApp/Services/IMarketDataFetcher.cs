namespace ValutaBot.MiniApp;

public interface IMarketDataFetcher
{
    MiniAppController.OhlcCandle[]? GetOhlcCandles(string key);
    void SetOhlcCandles(string key, MiniAppController.OhlcCandle[] candles);
    
    string IntervalMap(string tf);
    int GetExpiryCandles(string tf);
    int TimeframeSeconds(string tf);
    string? HigherTf(string tf);
    string? LowerTf(string tf);

    Task<(double[] prices, double[] volumes)> FetchBinanceCandles(string symbol, string interval, int limit = 50, string? rawAsset = null, string? rawInterval = null);
    Task<(double[] prices, double[] volumes)> FetchBinanceWithFallback(string? symbol, string rawInterval, string? originalAsset = null, int limit = 50, int cacheTtlSeconds = 10);
}
