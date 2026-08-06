using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ValutaBot.MiniApp.CQRS.Handlers;
using ValutaBot.App.MiniApp.Services;

namespace ValutaBot.MiniApp.CQRS.Handlers;

public class MarketAnalysisContext
{
    private static string _lastSeenModelVersion = "";
    
    private readonly GetMarketAnalysisQueryHandler _handler;
    private readonly string _asset;
    private readonly string _timeframe;
    
    // Properties to mimic local variables
    private string _clean;
    private string? _symbol;
    private bool _isForex;
    private bool _isMajor;
    private int _limit;
    private string _tfLower;
    private bool _useMultiTf;
    private string _mainInterval;
    private string? _higherTf;
    private string? _lowerTf;
    private double[] _mainPrices = Array.Empty<double>();
    private double[] _mainVolumes = Array.Empty<double>();
    private string _mainOhlcKey;
    private MiniAppController.OhlcCandle[]? _ohlcCandles;
    private (double[] prices, double[] volumes)? _higherResultData;
    private (double[] prices, double[] volumes)? _lowerResultData;
    
    private double _conflictPenalty = 1.0;

    private SmcEngine.SmcAnalysisResult _smcResult;
    private OrderFlowEngine.OrderFlowResult _orderFlowResult;
    private WalkForwardValidationEngine.WalkForwardResult _wfResult;

    private string _lgbmDirection = "NEUTRAL";
    private double _lgbmConfidence = 0.5;
    private string _lgbmModelVersion = "disabled";
    private double? _lgbmAccuracy = null;
    private string _llmReport = "LLM-СЃРІРѕРґРєР° Р·Р°РіСЂСѓР¶Р°РµС‚СЃСЏ...";
    
    private double _mainAdx, _mainPdi, _mainMdi, _mainAtr;
    private (double score, double confidence, double rsiVal, double emaVal, double volStrengthVal, double atrVal) _mainResult;
    private MiniAppController.OhlcCandle[]? _higherOhlcCandles;

    public MarketAnalysisContext(GetMarketAnalysisQueryHandler handler, string asset, string timeframe)
    {
        _handler = handler;
        _asset = asset;
        _timeframe = timeframe;
    }

    public async Task<object> ExecuteAnalysisAsync()
    {
        var swTotal = System.Diagnostics.Stopwatch.StartNew();
        long fetchMs = 0, mathMs = 0, mlMs = 0, matrixMs = 0;

        try
        {
            var swPhase = System.Diagnostics.Stopwatch.StartNew();
            await InitializeDataAsync();
            fetchMs = swPhase.ElapsedMilliseconds;

            if (_mainPrices == null || _mainPrices.Length == 0)
            {
                throw new Exception("РќРµРґРѕСЃС‚Р°С‚РѕС‡РЅРѕ РґР°РЅРЅС‹С… РґР»СЏ Р°РЅР°Р»РёР·Р°. Р‘РёСЂР¶Р° РёР»Рё РїСЂРѕРІР°Р№РґРµСЂ РІРµСЂРЅСѓР»Рё РїСѓСЃС‚РѕР№ СЂРµР·СѓР»СЊС‚Р°С‚.");
            }

            var gatekeeper = _handler._riskGatekeeper.ValidateMarketGatekeeper(_asset, _timeframe, _mainPrices, _ohlcCandles);
            if (!gatekeeper.IsTradeable)
            {
                BotLogger.Warn($"[Analysis] Gatekeeper aborted trade for {_asset} ({_timeframe}): {gatekeeper.Reason}");
                throw new Exception(gatekeeper.Reason);
            }

            swPhase.Restart();
            await AnalyzeCoreMechanicsAsync();
            await EvaluateTechnicalIndicatorsAsync();
            mathMs = swPhase.ElapsedMilliseconds;

            swPhase.Restart();
            await GatherMachineLearningAsync();
            mlMs = swPhase.ElapsedMilliseconds;

            swPhase.Restart();
            var finalResult = await BuildFinalConsensusAsync(fetchMs, mathMs, mlMs, matrixMs, swPhase, swTotal);
            return finalResult;
        }
        catch (ExchangeUnavailableException exEx)
        {
            MiniAppController.LastExceptionMessage = exEx.ToString();
            BotLogger.Warn($"[Analysis] Exchange unavailable for asset {_asset}: {exEx.Message}");
            throw;
        }

        catch (Exception ex)
        {
            MiniAppController.LastExceptionMessage = ex.ToString();
            BotLogger.Error($"[Analysis] Analysis failed for asset {_asset} on {_timeframe}", ex);
            throw;
        }
    }

    private async Task InitializeDataAsync()
    {
        _clean = AssetSanitizer.Sanitize(_asset);
        DayOfWeek day = DateTime.UtcNow.DayOfWeek;
        _symbol = AssetSanitizer.MapSymbolByDayOfWeek(_clean, day);

        _isForex = _symbol == null || _symbol == "EURUSDT" || _symbol == "GBPUSDT" || _symbol == "AUDUSDT";
        _isMajor = _symbol == "BTCUSDT" || _symbol == "ETHUSDT" || _symbol == "SOLUSDT";
        _limit = 100;
        _tfLower = _timeframe.ToLower().Trim();
        if (_tfLower == "s10" || _tfLower == "s15" || _tfLower == "s30") _limit = 130;
        else if (_tfLower == "m1" || _tfLower == "m2" || _tfLower == "m3" || _tfLower == "m5") _limit = 150;
        else if (_tfLower == "m15" || _tfLower == "m30" || _tfLower == "h1") _limit = 200;

        _useMultiTf = true;
        _mainInterval = _handler._fetcher.IntervalMap(_timeframe);
        _higherTf = _useMultiTf ? _handler._fetcher.HigherTf(_timeframe) : null;
        _lowerTf = _useMultiTf ? _handler._fetcher.LowerTf(_timeframe) : null;

        _mainOhlcKey = _symbol != null ? $"{_symbol}_{_mainInterval}" : $"{_clean}_{_mainInterval}";

        var mainTask = _handler._fetcher.FetchBinanceWithFallback(_symbol, _mainInterval, _clean, _limit);
        var ohlcTask = _handler._fetcher.FetchOhlcWithFallbackAsync(_symbol, _timeframe, _asset, _limit);

        var higherTask = _higherTf != null ? SafeFetch(_higherTf) : Task.FromResult<(double[] prices, double[] volumes)?>(null);
        var lowerTask = _lowerTf != null ? SafeFetch(_lowerTf) : Task.FromResult<(double[] prices, double[] volumes)?>(null);
        var higherOhlcTask = _higherTf != null ? SafeFetchOhlc(_higherTf) : Task.FromResult<MiniAppController.OhlcCandle[]?>(null);

        var extraTasks = new List<Task<(double[] prices, double[] volumes)?>>();
        string[] checkTfs = { "m1", "m5", "m15", "h1" };
        foreach (var cTf in checkTfs)
        {
            if (cTf != _timeframe && cTf != _higherTf && cTf != _lowerTf)
            {
                extraTasks.Add(SafeFetch(cTf));
            }
        }

        var allTasks = new List<Task> { mainTask, ohlcTask, higherTask, lowerTask, higherOhlcTask };
        allTasks.AddRange(extraTasks);
        await Task.WhenAll(allTasks);

        var mainResultTuple = mainTask.Result;
        _mainPrices = mainResultTuple.prices;
        _mainVolumes = mainResultTuple.volumes;
        
        _ohlcCandles = ohlcTask.IsCompletedSuccessfully ? ohlcTask.Result : Array.Empty<MiniAppController.OhlcCandle>();

        _higherResultData = higherTask.Result;
        _lowerResultData = lowerTask.Result;
        _higherOhlcCandles = higherOhlcTask.Result;
    }

    private async Task<MiniAppController.OhlcCandle[]?> SafeFetchOhlc(string tf)
    {
        try { return await _handler._fetcher.FetchOhlcWithFallbackAsync(_symbol, tf, _asset, _limit); }
        catch (Exception ex) { BotLogger.Warn($"[Analysis] Failed to fetch higher TF OHLC candles: {ex.Message}"); return null; }
    }

    private async Task<(double[] prices, double[] volumes)?> SafeFetch(string tf)
    {
        try { return await _handler._fetcher.FetchBinanceWithFallback(_symbol, tf, _asset, _limit); }
        catch (Exception ex) { Console.WriteLine($"[Fetch Warning] TF {tf} failed: {ex.Message}"); return null; }
    }

    private async Task AnalyzeCoreMechanicsAsync()
    {
        _smcResult = SmcEngine.AnalyzeSmcStructure(_asset, _mainInterval, _ohlcCandles ?? Array.Empty<MiniAppController.OhlcCandle>(), _mainPrices[^1]);
        BotLogger.Info($"[SMC Engine] Asset {_asset} ({_timeframe}): SMC Zones updated.");
        _orderFlowResult = OrderFlowEngine.AnalyzeOrderFlow(_asset, _mainInterval, _ohlcCandles ?? Array.Empty<MiniAppController.OhlcCandle>(), _mainPrices[^1]);
        BotLogger.Info($"[Order Flow] Asset {_asset} ({_timeframe}): {_orderFlowResult.Description}");
    }

    private async Task GatherMachineLearningAsync()
    {
        bool isNewsActive = false;

        _wfResult = _handler._wfEngine.ValidateWalkForward(_asset, _timeframe, _mainPrices, isNewsActive);
        if (_wfResult.IsOverfitted || _wfResult.IsCooloffActive)
        {
            BotLogger.Warn($"[Anti-Overfitting] {_asset} ({_timeframe}): {_wfResult.StatusReasoning} ML weight multiplier set to {_wfResult.WeightMultiplier}x.");
        }

        if (_ohlcCandles != null && _ohlcCandles.Length >= 60)
        {
            try
            {
                var prediction = await MLPythonService.PredictAsync(_asset, _timeframe, _ohlcCandles, _isForex);
                if (prediction != null && prediction.Direction != "NEUTRAL")
                {
                    _lgbmDirection = prediction.Direction;
                    _lgbmConfidence = (float)(prediction.Confidence * _wfResult.WeightMultiplier);
                    _lgbmConfidence = Math.Clamp(_lgbmConfidence, 0f, 1f);

                    // META-LABELING: If ML confidence is weak (or heavily penalized by WalkForward), neutralize it entirely.
                    if (_lgbmConfidence < 0.51f)
                    {
                        BotLogger.Info($"[ML Override] WalkForward suppressed ML confidence to {_lgbmConfidence:F2}. Reverting to pure Math.");
                        _lgbmDirection = "NEUTRAL";
                    }
                    else
                    {
                        BotLogger.Info($"[ML Override] ML confident ({_lgbmConfidence:F2}). Taking full control of the directional vector.");
                    }

                    _lgbmModelVersion = prediction.ModelVersion;
                    _lgbmAccuracy = prediction.Accuracy;

                    // ── ML Telemetry: Global Retraining ──
                    if (!string.IsNullOrEmpty(prediction.ModelVersion))
                    {
                        string oldVer = System.Threading.Interlocked.Exchange(ref _lastSeenModelVersion, prediction.ModelVersion);
                        if (oldVer != prediction.ModelVersion)
                        {

                            // Skip the very first startup assignment spam, only alert on actual changes during runtime
                            if (!string.IsNullOrEmpty(oldVer))
                            {
                                _ = Task.Run(async () =>
                                {
                                    try
                                    {
                                        string accStr = prediction.Accuracy.HasValue ? $"{prediction.Accuracy.Value * 100:F1}%" : "N/A";
                                        string aucStr = prediction.Auc.HasValue ? $"{prediction.Auc.Value:F3}" : "N/A";

                                        string report = $"[🧠 ML Global Retrain Detected]\n" +
                                                        $"Asset: {_asset}\n" +
                                                        $"New Model: <code>{prediction.ModelVersion}</code>\n" +
                                                        $"Previous: <code>{oldVer}</code>\n\n" +
                                                        $"🔹 Cross-Validation Accuracy: {accStr}\n" +
                                                        $"🔹 AUC-ROC Score: {aucStr}";

                                        await TelegramBotService.SendMessageToAdmins(report);

                                        string logDir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
                                        System.IO.Directory.CreateDirectory(logDir);
                                        string logFile = System.IO.Path.Combine(logDir, "ml_global_retrain.csv");
                                        bool writeHeader = !System.IO.File.Exists(logFile);
                                        using var writer = new System.IO.StreamWriter(logFile, append: true);
                                        if (writeHeader) await writer.WriteLineAsync("Timestamp,Asset,OldVersion,NewVersion,Accuracy,Auc");
                                        await writer.WriteLineAsync($"{DateTime.UtcNow:O},{_asset},{oldVer},{prediction.ModelVersion},{prediction.Accuracy},{prediction.Auc}");
                                    }
                                    catch (Exception tEx)
                                    {
                                        BotLogger.Error("[MarketAnalysis] Error sending ML global telemetry", tEx);
                                    }
                                });
                            }
                        }
                    }
                }

                var llmService = new LlmReportingService();
                var regime = ContinuousStateEngine.EvaluateContinuousState(_mainPrices, _asset, _timeframe).VelocityRegime;
                bool isUp = _lgbmDirection == "BUY";
                bool isTaUp = _mainResult.score > 0;
                bool isOfUp = _orderFlowResult.ScoreContribution > 0;
                _llmReport = llmService.GenerateMarketSummary(_asset, regime, prediction, isUp, isTaUp, isOfUp);
            }
            catch (Exception ex) 
            { 
                Console.WriteLine($"[Python ML Warning] {ex.GetType().Name}: {ex.Message}");
                _llmReport = $"вљ пёЏ ML-РґРІРёР¶РѕРє РЅРµРґРѕСЃС‚СѓРїРµРЅ: {ex.GetType().Name} вЂ” {ex.Message}";
            }
        }
    }

    private async Task EvaluateTechnicalIndicatorsAsync()
    {
        (_mainAdx, _mainPdi, _mainMdi) = _ohlcCandles != null ? _handler._mathEngine.ComputeTrueAdx(_asset, _timeframe, _ohlcCandles) : (20.0, 0.0, 0.0);
        _mainAtr = _ohlcCandles != null ? _handler._mathEngine.ComputeAtr(_asset, _timeframe, _ohlcCandles) : 0;

        _mainResult = _handler._marketAnalyzer.ScoreTimeframe(_asset, _timeframe, _mainPrices, _mainVolumes ?? Array.Empty<double>(), candles: _ohlcCandles, adxOverride: _mainAdx, atrOverride: _mainAtr, isForex: _isForex);

        if (_higherResultData != null)
        {
            var higherOhlcKey = _higherTf != null ? (_symbol != null ? $"{_symbol}_{_handler._fetcher.IntervalMap(_higherTf)}" : $"{_clean}_{_handler._fetcher.IntervalMap(_higherTf)}") : null;
            
            if (_higherResultData.HasValue && _higherOhlcCandles != null)
            {
                var htfSmcResult = SmcEngine.AnalyzeSmcStructure(_asset, _higherTf ?? "", _higherOhlcCandles, _higherResultData.Value.prices.Length > 0 ? _higherResultData.Value.prices[^1] : 0.0);
                var mtfValidation = SmcEngine.ValidateMtfSmcAlignment(_smcResult, htfSmcResult);
                _conflictPenalty *= mtfValidation.ConfluenceMultiplier;
                BotLogger.Info($"[MTF SMC Validation] Alignment: {mtfValidation.AlignmentStatus} | Multiplier={mtfValidation.ConfluenceMultiplier:F2}x");
            }

            var (hAdx, hPdi, hMdi) = _higherOhlcCandles != null ? _handler._mathEngine.ComputeTrueAdx(_asset, _higherTf ?? "", _higherOhlcCandles) : (20.0, 0.0, 0.0);
            double hAtr = _higherOhlcCandles != null ? _handler._mathEngine.ComputeAtr(_asset, _higherTf ?? "", _higherOhlcCandles) : 0;
            var higherResult = _handler._marketAnalyzer.ScoreTimeframe(_asset, _higherTf ?? "", _higherResultData.Value.prices, _higherResultData.Value.volumes ?? Array.Empty<double>(), candles: _higherOhlcCandles, adxOverride: hAdx, atrOverride: hAtr, isForex: _isForex);
            
            _conflictPenalty *= GetMarketAnalysisQueryHandler.MfConflictPenalty(_mainResult, higherResult);
        }
    }

    private async Task<object> BuildFinalConsensusAsync(long fetchMs, long mathMs, long mlMs, long matrixMs, System.Diagnostics.Stopwatch swPhase, System.Diagnostics.Stopwatch swTotal)
    {
        bool isSubMinute = _timeframe.ToLower().StartsWith("s");
        
        // Construct Signals for the Confluence Matrix
        var taSignal = new TaSignal(_mainResult.score, _mainResult.confidence, _mainResult.rsiVal, _mainResult.emaVal, _mainResult.volStrengthVal, _mainAtr, _mainAdx);
        var smcSignal = new SmcSignal(_smcResult.BosDirection, _smcResult.SweepDirection, _smcResult.OrderBlockType, _smcResult.FvgType, "SMC Analyzed");
        var ofSignal = new OrderflowSignal(_orderFlowResult.ScoreContribution, _orderFlowResult.Description);
        var mlSignal = new MlSignal(_lgbmDirection, _lgbmConfidence, _lgbmAccuracy, _lgbmModelVersion);
        
        var continuousState = ContinuousStateEngine.EvaluateContinuousState(_mainPrices, _asset, _timeframe);
        var stateSignal = new StateSignal(continuousState.VelocityRegime.ToString(), continuousState.VelocityBpsPerSec, continuousState.MomentumContribution);

        var mtfResult = await _handler._cmEngine.Evaluate4DMatrixAsync(_asset, _timeframe, _isForex, _symbol);

        var consensus = await _handler._cmEngine.EvaluateMatrixAsync(
            _asset, _timeframe, isSubMinute, _conflictPenalty, 
            taSignal, smcSignal, ofSignal, mlSignal, stateSignal, mtfResult
        );

        matrixMs = swPhase.ElapsedMilliseconds;
        swTotal.Stop();
        
        string latencyLog = $"[Latency] Total: {swTotal.ElapsedMilliseconds}ms | Fetch: {fetchMs}ms | Math: {mathMs}ms | ML: {mlMs}ms | Matrix: {matrixMs}ms";
        BotLogger.Info(latencyLog);

        string finalDirection = consensus.FinalDirection;
        int finalProbability = consensus.Probability;
        
        int timeframeSec = _handler._fetcher.TimeframeSeconds(_timeframe);
        double volRatio = _handler._marketAnalyzer.CalculateVolatilityRatio(_mainPrices);
        var timeoutResult = _handler._timeoutEngine.CalculateTimeout(_asset, _timeframe, _mainAtr, volRatio, _smcResult, _mainPrices[^1]);
        
        // --- PRODUCTION KILL SWITCH (Pre-Simulation) ---
        if (_wfResult.IsCooloffActive)
        {
            var remaining = _wfResult.CooloffUntil - DateTime.UtcNow;
            int mins = Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes));
            BotLogger.Warn($"[KillSwitch] Blocked trade for {_asset} {_timeframe} due to WFE Cooloff. Resumes in {mins} min.");
            throw new Exception(
                $"\u26a0\ufe0f \u0410\u043d\u0430\u043b\u0438\u0437\u0430\u0442\u043e\u0440 \u0437\u0430\u0431\u043b\u043e\u043a\u0438\u0440\u043e\u0432\u0430\u043d: 3 \u043f\u043e\u0441\u043b\u0435\u0434\u043e\u0432\u0430\u0442\u0435\u043b\u044c\u043d\u044b\u0445 \u0443\u0431\u044b\u0442\u043a\u0430 (\u0414\u0440\u043e\u0443\u0434\u0430\u0443\u043d \u043f\u0440\u043e\u0442\u0435\u043a\u0446\u0438\u044f). " +
                $"\u23f3 \u0420\u0430\u0437\u0431\u043b\u043e\u043a\u0438\u0440\u043e\u0432\u043a\u0430 \u0447\u0435\u0440\u0435\u0437: {mins} \u043c\u0438\u043d. (\u0432 {_wfResult.CooloffUntil:HH:mm} UTC). " +
                "\u041f\u043e\u0434\u043e\u0436\u0434\u0438\u0442\u0435 \u0438\u043b\u0438 \u0441\u043c\u0435\u043d\u0438\u0442\u0435 \u0430\u043a\u0442\u0438\u0432, \u0447\u0442\u043e\u0431\u044b \u043f\u0440\u043e\u0434\u043e\u043b\u0436\u0438\u0442\u044c \u0430\u043d\u0430\u043b\u0438\u0437.");
        }

        var mcResult = finalDirection == "NEUTRAL" 
            ? new MonteCarloResult(0, 0, 0, 0, "Blocked", "Blocked", "Trade blocked before simulation")
            : _handler._mcEngine.Simulate(_mainPrices[^1], finalProbability / 100.0, finalDirection, _mainAtr, timeoutResult.TimeoutCandles * timeframeSec, 0.85, 1000);

        string orderFlowDir = _orderFlowResult.ScoreContribution > 0 ? "BUY" : _orderFlowResult.ScoreContribution < 0 ? "PUT" : "NEUTRAL";
        
        await SignalTracker.RecordPredictionAsync(
            finalDirection, _asset, _timeframe, _mainPrices[^1],
            expiryCandles: timeoutResult.TimeoutCandles,
            timeframeSecs: timeframeSec, isForex: _isForex, binanceSymbol: _symbol,
            sourceDirections: new Dictionary<string, string> {
                ["LIGHTGBM"] = _lgbmDirection, ["SKENDER_MATH"] = consensus.FinalTotalScore > 0.02 ? "BUY" : consensus.FinalTotalScore < -0.02 ? "PUT" : "NEUTRAL",
                ["SMC"] = (smcSignal.SweepDirection ?? "").Contains("BULLISH") ? "BUY" : (smcSignal.SweepDirection ?? "").Contains("BEARISH") ? "PUT" : "NEUTRAL", ["ORDERFLOW"] = orderFlowDir,
                ["NATIVE_ML"] = "NEUTRAL"
            }
        );

        var overallStats = await SignalTracker.GetOverallStatsAsync();
        var assetStats   = await SignalTracker.GetStatsAsync(_asset, _timeframe);

        return new
        {
            direction = finalDirection,
            probability = finalProbability,
            duration = timeoutResult.TimeoutText,
            adaptiveReasoning = $"{timeoutResult.Reasoning} | {mtfResult.SummaryReasoning}",
            goldenSetup = mtfResult.IsGoldenSetup,
            confluenceLabel = mtfResult.ConfluenceLabel,
            confluenceRatio = mtfResult.ConfluenceRatio,
            expiryCandles = timeoutResult.TimeoutCandles,
            chartData = _mainPrices,
            rsi = Math.Round(_mainResult.rsiVal, 1),
            ema = Math.Round(_mainResult.emaVal, 2),
            volumeStrength = Math.Round(_mainResult.volStrengthVal, 2),
            tfConflict = _conflictPenalty < 1.0,
            lgbmDirection = _lgbmDirection,
            lgbmConfidence = Math.Round(_lgbmConfidence * 100, 0),
            lgbmAccuracy = _lgbmAccuracy.HasValue ? Math.Round(_lgbmAccuracy.Value * 100, 1) : (double?)null,
            lgbmModelVersion = _lgbmModelVersion,
            newsSentiment = "Neutral", // Removed old logic
            newsScore = 0.0,
            newsSummary = "",
            newsHeadlines = Array.Empty<string>(),
            claudeReasoning = consensus.CombinedReasoningText,
            winRateOverall = overallStats.HasData ? overallStats.WinRate : (double?)null,
            winRateAsset = assetStats.HasData ? assetStats.WinRate : (double?)null,
            signalsVerified = overallStats.Verified,
            signalsPending = await SignalTracker.GetPendingCountAsync(),
            monteCarloIterations = mcResult.Iterations,
            monteCarloSuccess = mcResult.SuccessCount,
            evPct = mcResult.ExpectedValuePct,
            evLabel = mcResult.EvLabel,
            kellyRiskPct = mcResult.KellyRiskPct,
            kellyLabel = mcResult.KellyLabel,
            monteCarloSummary = mcResult.SummaryReasoning,
            wfIsCooloffActive = _wfResult.IsCooloffActive,
            llmReport = _llmReport,
            latencies = new { 
                total = swTotal.ElapsedMilliseconds, 
                fetch = fetchMs, 
                math = mathMs, 
                ml = mlMs, 
                matrix = matrixMs 
            }
        };
    }
}
