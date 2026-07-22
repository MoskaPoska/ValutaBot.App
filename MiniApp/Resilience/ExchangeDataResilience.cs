namespace ValutaBot.MiniApp;

public class ExchangeUnavailableException : Exception
{
    public string UserFriendlyMessage { get; }

    public ExchangeUnavailableException(string message, string userFriendlyMessage, Exception? inner = null)
        : base(message, inner)
    {
        UserFriendlyMessage = userFriendlyMessage;
    }
}

public class MarketClosedException : Exception
{
    public string UserFriendlyMessage { get; }

    public MarketClosedException(string message, string userFriendlyMessage)
        : base(message)
    {
        UserFriendlyMessage = userFriendlyMessage;
    }
}

public static class ExchangeDataResilience
{
    private const int MaxTimeoutSeconds = 12;

    public static async Task<(double[] prices, double[] volumes)> FetchPricesResilientAsync(
        string? symbol,
        string interval,
        string rawAsset,
        int limit,
        int cacheTtlSeconds = 10)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(MaxTimeoutSeconds));
        try
        {
            var fetchTask = MarketDataFetcher.FetchBinanceWithFallback(symbol, interval, rawAsset, limit, cacheTtlSeconds);
            var completedTask = await Task.WhenAny(fetchTask, Task.Delay(TimeSpan.FromSeconds(MaxTimeoutSeconds), cts.Token));

            if (completedTask != fetchTask)
            {
                BotLogger.Warn($"[Resilience] Exchange data timeout exceeded ({MaxTimeoutSeconds}s) for asset: {rawAsset}");
                throw new ExchangeUnavailableException(
                    $"Timeout fetching prices for {rawAsset}",
                    "⚠️ Сервер биржи временно недоступен, попробуйте через минуту."
                );
            }

            var result = await fetchTask;
            if (result.prices == null || result.prices.Length == 0)
            {
                BotLogger.Warn($"[Resilience] Empty price array returned for asset: {rawAsset}");
                throw new ExchangeUnavailableException(
                    $"Empty prices for {rawAsset}",
                    "⚠️ Данные по выбранному активу временно недоступны. Попробуйте через минуту."
                );
            }

            cts.Cancel();
            return result;
        }
        catch (ExchangeUnavailableException)
        {
            throw;
        }
        catch (TaskCanceledException ex)
        {
            BotLogger.Warn($"[Resilience] Task canceled while fetching {rawAsset}", ex);
            throw new ExchangeUnavailableException(
                $"Canceled fetching {rawAsset}",
                "⚠️ Сервер биржи временно недоступен, попробуйте через минуту.",
                ex
            );
        }
        catch (HttpRequestException ex)
        {
            BotLogger.Error($"[Resilience] Network HTTP error fetching {rawAsset}", ex);
            throw new ExchangeUnavailableException(
                $"HTTP failure for {rawAsset}",
                "⚠️ Ошибка связи с биржевым сервером. Попробуйте через минуту.",
                ex
            );
        }
        catch (Exception ex)
        {
            BotLogger.Error($"[Resilience] Unexpected error fetching market data for {rawAsset}", ex);
            throw new ExchangeUnavailableException(
                $"Unexpected error fetching {rawAsset}",
                "⚠️ Произошел технический сбой. Попробуйте через минуту.",
                ex
            );
        }
    }
}
