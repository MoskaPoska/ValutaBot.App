namespace ValutaBot.MiniApp;

/// <summary>
/// Handles asset ticker normalization, Cyrillic OTC conversion, and exchange symbol mapping.
/// </summary>
public static class AssetSanitizer
{
    /// <summary>
    /// Clean and normalize user-provided asset names (e.g. "EUR/USD OTC" or "EUR/USD ОТС" -> "EURUSD").
    /// </summary>
    public static string Sanitize(string? asset)
    {
        if (string.IsNullOrWhiteSpace(asset)) return "EURUSD";
        return asset.ToUpperInvariant()
            .Replace("ОТС", "OTC") // Replace Cyrillic OTC
            .Replace("отс", "OTC")
            .Replace("OTC", "")
            .Replace(" ", "")
            .Replace("/", "")
            .Replace("-", "")
            .Replace("_", "")
            .Trim();
    }

    /// <summary>
    /// Map normalized asset to Binance symbol on weekends or return null for TwelveData fetching.
    /// </summary>
    public static string? MapSymbolByDayOfWeek(string cleanAsset, DayOfWeek day)
    {
        bool isWeekend = day == DayOfWeek.Saturday || day == DayOfWeek.Sunday;
        if (!isWeekend) return null; // 100% TwelveData on weekdays

        return cleanAsset switch
        {
            "BTCUSDT" or "BTC" or "BTCUSD" => "BTCUSDT",
            "ETHUSDT" or "ETH" or "ETHUSD" => "ETHUSDT",
            "SOLUSDT" or "SOL" or "SOLUSD" => "SOLUSDT",
            "EURUSD" or "EURUSDT" => "EURUSDT",
            "GBPUSD" or "GBPUSDT" => "GBPUSDT",
            "AUDUSD" or "AUDUSDT" => "AUDUSDT",
            _ => null
        };
    }
}
