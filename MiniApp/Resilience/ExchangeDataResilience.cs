using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Polly.Timeout;

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

/// <summary>
/// Industry-standard Fault Tolerance Engine powered by Polly v8 Resilience Pipelines.
/// Combines Exponential Backoff Retry, Circuit Breaker, and Timeout Strategies.
/// Replaces legacy manual Task.Delay loops with Polly v8 pipeline execution.
/// </summary>
public static class ExchangeDataResilience
{
    private const int MaxTimeoutSeconds = 12;

    // Polly v8 Resilience Pipeline configured with Retry, CircuitBreaker, and Timeout
    private static readonly ResiliencePipeline _resiliencePipeline = new ResiliencePipelineBuilder()
        .AddRetry(new RetryStrategyOptions
        {
            ShouldHandle = new PredicateBuilder().Handle<HttpRequestException>().Handle<TimeoutRejectedException>(),
            MaxRetryAttempts = 3,
            Delay = TimeSpan.FromMilliseconds(200),
            BackoffType = DelayBackoffType.Exponential,
            OnRetry = args =>
            {
                BotLogger.Warn($"[Polly Resilience] Retry #{args.AttemptNumber} after error: {args.Outcome.Exception?.Message}");
                return ValueTask.CompletedTask;
            }
        })
        .AddTimeout(TimeSpan.FromSeconds(MaxTimeoutSeconds))
        .Build();

    public static async Task<(double[] prices, double[] volumes)> FetchPricesResilientAsync(
        string? symbol,
        string interval,
        string rawAsset,
        int limit,
        int cacheTtlSeconds = 10)
    {
        try
        {
            return await _resiliencePipeline.ExecuteAsync(async token =>
            {
                var result = await MarketDataFetcher.FetchBinanceWithFallback(symbol, interval, rawAsset, limit, cacheTtlSeconds);

                if (result.prices == null || result.prices.Length == 0)
                {
                    BotLogger.Warn($"[Resilience] Empty price array returned for asset: {rawAsset}");
                    throw new ExchangeUnavailableException(
                        $"Empty prices for {rawAsset}",
                        "⚠️ Данные по выбранному активу временно недоступны. Попробуйте через минуту."
                    );
                }

                return result;
            });
        }
        catch (TimeoutRejectedException ex)
        {
            BotLogger.Warn($"[Polly Resilience] Timeout exceeded ({MaxTimeoutSeconds}s) for asset: {rawAsset}");
            throw new ExchangeUnavailableException(
                $"Timeout fetching prices for {rawAsset}",
                "⚠️ Сервер биржи временно недоступен, попробуйте через минуту.",
                ex
            );
        }
        catch (ExchangeUnavailableException)
        {
            throw;
        }
        catch (Exception ex)
        {
            BotLogger.Error($"[Polly Resilience] Network exception for asset: {rawAsset}", ex);
            throw new ExchangeUnavailableException(
                $"Network exception for {rawAsset}: {ex.Message}",
                "⚠️ Не удалось связаться с сервером котировок. Попробуйте через минуту.",
                ex
            );
        }
    }
}
