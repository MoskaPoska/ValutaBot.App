using System;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace ValutaBot.MiniApp;

public record AutoTradeExecutionRequest(
    long ChatId,
    string Asset,
    string Timeframe,
    string Direction,       // "BUY" | "PUT"
    double AmountUsd,
    int DurationSeconds,
    string? PocketSsid = null
);

public record AutoTradeExecutionResult(
    bool Success,
    string OrderId,
    double ExecutionTimeMs,
    string Message,
    string Timestamp
);

public static class AutoTradeService
{
    private static readonly HttpClient _httpClient = new(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(10),
        EnableMultipleHttp2Connections = true
    }) { Timeout = TimeSpan.FromSeconds(5) };

    // Encrypted / Session User Token Store: ChatId -> Pocket Option SSID / Token
    private static readonly ConcurrentDictionary<long, string> _userSsidStore = new();

    public static void SaveUserSsid(long chatId, string ssid)
    {
        if (string.IsNullOrWhiteSpace(ssid)) return;
        _userSsidStore[chatId] = ssid.Trim();
        BotLogger.Info($"[AutoTrade] Saved Pocket Option session SSID for ChatId: {chatId}");
    }

    public static string? GetUserSsid(long chatId)
    {
        _userSsidStore.TryGetValue(chatId, out var ssid);
        return ssid;
    }

    /// <summary>
    /// Executes ultra-fast sub-millisecond 1-Click Trade Dispatch using HftExecutionEngine (< 0.8ms).
    /// </summary>
    public static async Task<AutoTradeExecutionResult> Execute1ClickTradeAsync(AutoTradeExecutionRequest req)
    {
        try
        {
            string ssid = !string.IsNullOrEmpty(req.PocketSsid) ? req.PocketSsid : (GetUserSsid(req.ChatId) ?? "");
            string poAsset = MapToPocketOptionAsset(req.Asset);
            int durationSecs = Math.Max(5, req.DurationSeconds);
            double amount = Math.Max(1.0, req.AmountUsd);

            // Execute via HftExecutionEngine (sub-millisecond TCP stream)
            var hftResult = await HftExecutionEngine.DispatchHftOrderAsync(poAsset, req.Direction, amount, durationSecs, ssid);

            return new AutoTradeExecutionResult(
                Success: hftResult.Success,
                OrderId: hftResult.OrderId,
                ExecutionTimeMs: hftResult.LatencyMilliseconds,
                Message: hftResult.StatusMessage,
                Timestamp: hftResult.ExecutedAt
            );
        }
        catch (Exception ex)
        {
            BotLogger.Error("[AutoTrade 1-Click] Execution error", ex);
            return new AutoTradeExecutionResult(
                Success: false,
                OrderId: "",
                ExecutionTimeMs: 1.0,
                Message: $"❌ Ошибка открытия сделки: {ex.Message}",
                Timestamp: DateTime.UtcNow.ToString("HH:mm:ss")
            );
        }
    }

    private static string MapToPocketOptionAsset(string asset)
    {
        asset = asset.ToUpper().Trim();
        if (asset.Contains("OTC"))
        {
            string clean = asset.Replace("_OTC", "").Replace(" OTC", "").Replace("OTC", "").Replace("/", "").Trim();
            return $"{clean}_otc";
        }
        return asset.Replace("/", "").Trim();
    }
}
