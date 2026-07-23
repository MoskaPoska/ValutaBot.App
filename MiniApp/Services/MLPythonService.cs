using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ValutaBot.MiniApp;

/// <summary>
/// Calls the Python LightGBM ML microservice via HTTP.
/// Static service using the shared HttpClient from MiniAppController.
/// Falls back gracefully if the service is unavailable.
/// </summary>
public static class MLPythonService
{
    private static readonly HttpClient _client = new HttpClient
    {
        Timeout = TimeSpan.FromSeconds(8)  // fast timeout — don't block the main analysis
    };

    private static string _baseUrl = string.Empty;
    private static volatile bool _available = true;       // circuit-breaker flag
    private static DateTime _nextRetry = DateTime.MinValue;
    private static readonly TimeSpan CircuitOpenDuration = TimeSpan.FromMinutes(3);
    private static readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    // ── Result type ────────────────────────────────────────────────────────

    public record MLPythonPrediction(
        string Direction,       // "BUY" | "PUT" | "NEUTRAL"
        double Confidence,      // 0.0 – 1.0
        string ModelVersion,    // e.g. "lgbm-v1-BTCUSDT_1m-1720000000"
        double? Accuracy,       // CV accuracy (null if model not yet trained)
        double? Auc             // CV AUC-ROC
    );

    // ── Init ───────────────────────────────────────────────────────────────

    public static void Init(string? baseUrl)
    {
        _baseUrl = (baseUrl ?? string.Empty).TrimEnd('/');
        if (!string.IsNullOrWhiteSpace(_baseUrl))
        {
            Console.WriteLine($"[MLPython] Service URL: {_baseUrl}");
            if (_baseUrl.Contains("localhost") || _baseUrl.Contains("127.0.0.1"))
            {
                EnsureLocalPythonServiceRunning();
            }
        }
        else
        {
            Console.WriteLine("[MLPython] No ML_SERVICE_URL configured — Python ML disabled.");
        }
    }

    private static void EnsureLocalPythonServiceRunning()
    {
        Task.Run(async () =>
        {
            try
            {
                using var testClient = new HttpClient { Timeout = TimeSpan.FromSeconds(1) };
                var res = await testClient.GetAsync($"{_baseUrl}/health");
                if (res.IsSuccessStatusCode)
                {
                    Console.WriteLine("[MLPython] Local LightGBM service is active.");
                    return;
                }
            }
            catch
            {
                // Not running yet -> try launching
            }

            try
            {
                string mlDir = Path.Combine(Directory.GetCurrentDirectory(), "ml_service");
                string mainScript = Path.Combine(mlDir, "main.py");

                if (!File.Exists(mainScript))
                {
                    mlDir = Path.Combine(AppContext.BaseDirectory, "ml_service");
                    mainScript = Path.Combine(mlDir, "main.py");
                }

                if (File.Exists(mainScript))
                {
                    Console.WriteLine("[MLPython] Auto-starting Python LightGBM microservice...");
                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "py",
                        Arguments = $"\"{mainScript}\"",
                        WorkingDirectory = mlDir,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    System.Diagnostics.Process.Start(psi);
                    Console.WriteLine("[MLPython] Python LightGBM service started in background!");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MLPython] Local auto-start notice: {ex.Message}");
            }
        });
    }

    // ── Public API ─────────────────────────────────────────────────────────

    /// <summary>
    /// Send candles to the Python ML service and receive a direction prediction.
    /// Returns null if the service is unavailable or disabled.
    /// </summary>
    public static async Task<MLPythonPrediction?> PredictAsync(
        string symbol,
        string interval,
        MiniAppController.OhlcCandle[] candles,
        bool isForex = false)
    {
        if (string.IsNullOrWhiteSpace(_baseUrl))
            return null;

        // Circuit-breaker: skip if recently failed
        if (!_available && DateTime.UtcNow < _nextRetry)
            return null;

        try
        {
            // Map ValutaBot asset name to Binance-style symbol
            var binanceSymbol = MapSymbol(symbol, isForex);

            // Build request payload
            var candleList = candles.Select(c => new
            {
                open = c.Open,
                high = c.High,
                low = c.Low,
                close = c.Close,
                volume = c.Volume
            }).ToArray();

            var payload = new
            {
                symbol = binanceSymbol,
                interval = interval,
                candles = candleList,
                is_forex = isForex
            };

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _client.PostAsync($"{_baseUrl}/predict", content);

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"[MLPython] Non-OK response: {(int)response.StatusCode}");
                return null;
            }

            var body = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<PredictResponseDto>(body, _jsonOptions);

            if (result == null)
                return null;

            // Reset circuit breaker on success
            _available = true;

            Console.WriteLine($"[MLPython] {binanceSymbol}/{interval} → {result.Direction} " +
                              $"conf={result.Confidence:F2} model={result.ModelVersion}");

            return new MLPythonPrediction(
                Direction:    result.Direction ?? "NEUTRAL",
                Confidence:   result.Confidence,
                ModelVersion: result.ModelVersion ?? "unknown",
                Accuracy:     result.Accuracy,
                Auc:          result.Auc
            );
        }
        catch (TaskCanceledException)
        {
            Console.WriteLine("[MLPython] Request timed out — skipping");
            OpenCircuit();
            return null;
        }
        catch (HttpRequestException ex)
        {
            Console.WriteLine($"[MLPython] Connection error: {ex.Message}");
            OpenCircuit();
            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MLPython] Unexpected error: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Sends real verified trade outcome (Win/Loss) to Python ML service for online reinforcement learning.
    /// </summary>
    public static async Task RecordOnlineTradeOutcomeAsync(
        string asset,
        string timeframe,
        double entryPrice,
        double exitPrice,
        string direction,
        bool wasWin)
    {
        if (string.IsNullOrWhiteSpace(_baseUrl)) return;
        if (!_available && DateTime.UtcNow < _nextRetry) return;

        try
        {
            var payload = new
            {
                asset,
                timeframe,
                entry_price = entryPrice,
                exit_price = exitPrice,
                direction,
                was_win = wasWin,
                timestamp = DateTime.UtcNow.ToString("o")
            };

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _client.PostAsync($"{_baseUrl}/feedback", content);
            if (response.IsSuccessStatusCode)
            {
                Console.WriteLine($"[MLPython] Online RL feedback registered for {asset}/{timeframe} -> {(wasWin ? "WIN" : "LOSS")}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MLPython] Online RL feedback notice: {ex.Message}");
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static void OpenCircuit()
    {
        _available = false;
        _nextRetry = DateTime.UtcNow + CircuitOpenDuration;
        Console.WriteLine($"[MLPython] Circuit opened, retry after {_nextRetry:HH:mm:ss}");
    }

    /// <summary>
    /// Map ValutaBot internal symbol to Binance-style uppercase symbol.
    /// Forex pairs are returned as-is (service handles them gracefully).
    /// </summary>
    private static string MapSymbol(string asset, bool isForex)
    {
        if (isForex)
        {
            // For forex, use EURUSD-style (Python service uses it as a key for model storage)
            return asset.Replace("/", "").Replace(" ", "").Replace("OTC", "")
                        .Replace("otc", "").Trim().ToUpperInvariant();
        }

        return asset.ToUpperInvariant() switch
        {
            "BTC" or "BITCOIN"  => "BTCUSDT",
            "ETH" or "ETHEREUM" => "ETHUSDT",
            "SOL" or "SOLANA"   => "SOLUSDT",
            "BNB"               => "BNBUSDT",
            "XRP"               => "XRPUSDT",
            "ADA"               => "ADAUSDT",
            "DOGE"              => "DOGEUSDT",
            _                   => asset.ToUpperInvariant().EndsWith("USDT")
                                       ? asset.ToUpperInvariant()
                                       : asset.ToUpperInvariant() + "USDT"
        };
    }

    // ── DTO ────────────────────────────────────────────────────────────────

    private class PredictResponseDto
    {
        [JsonPropertyName("direction")]    public string?  Direction    { get; set; }
        [JsonPropertyName("confidence")]   public double   Confidence   { get; set; }
        [JsonPropertyName("model_version")]public string?  ModelVersion { get; set; }
        [JsonPropertyName("accuracy")]     public double?  Accuracy     { get; set; }
        [JsonPropertyName("auc")]          public double?  Auc          { get; set; }
    }
}
