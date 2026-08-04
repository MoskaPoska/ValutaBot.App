using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ValutaBot.MiniApp.CQRS.Handlers;
using ValutaBot.App.MiniApp.Services;

namespace ValutaBot.MiniApp.CQRS.Handlers;

public class MarketAnalysisContext
{
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

    public MarketAnalysisContext(GetMarketAnalysisQueryHandler handler, string asset, string timeframe)
    {
        _handler = handler;
        _asset = asset;
        _timeframe = timeframe;
    }

    public async Task<object> ExecuteAnalysisAsync()
    {
        try
        {
            await InitializeDataAsync();

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

            await AnalyzeCoreMechanicsAsync();
            await GatherMachineLearningAsync();
            await EvaluateTechnicalIndicatorsAsync();

            return await BuildFinalConsensusAsync();
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

        var mainResultTuple = await _handler._fetcher.FetchBinanceWithFallback(_symbol, _mainInterval, _clean, _limit);
        _mainPrices = mainResultTuple.prices;
        _mainVolumes = mainResultTuple.volumes;

        try
        {
            _ohlcCandles = await _handler._fetcher.FetchOhlcWithFallbackAsync(_symbol, _timeframe, _asset, _limit);
        }
        catch (Exception ex)
        {
            BotLogger.Warn($"[Analysis] Failed to fetch OHLC candles: {ex.Message}");
            _ohlcCandles = Array.Empty<MiniAppController.OhlcCandle>();
        }

        var higherTask = _higherTf != null ? SafeFetch(_higherTf) : Task.FromResult<(double[] prices, double[] volumes)?>(null);
        var lowerTask = _lowerTf != null ? SafeFetch(_lowerTf) : Task.FromResult<(double[] prices, double[] volumes)?>(null);

        var extraTasks = new List<Task<(double[] prices, double[] volumes)?>>();
        if (_isMajor)
        {
            string[] checkTfs = { "m1", "m5", "m15", "h1" };
            foreach (var cTf in checkTfs)
            {
                if (cTf != _timeframe && cTf != _higherTf && cTf != _lowerTf)
                {
                    extraTasks.Add(SafeFetch(cTf));
                }
            }
        }

        await Task.WhenAll(higherTask, lowerTask);
        if (extraTasks.Count > 0) await Task.WhenAll(extraTasks);

        _higherResultData = await higherTask;
        _lowerResultData = await lowerTask;
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
                    _lgbmConfidence = (float)prediction.Confidence;
                    _lgbmModelVersion = prediction.ModelVersion;
                    _lgbmAccuracy = prediction.Accuracy;
                }

                var llmService = new LlmReportingService();
                var regime = ContinuousStateEngine.EvaluateContinuousState(_mainPrices, _asset, _timeframe).VelocityRegime;
                bool isUp = _lgbmDirection == "BUY";
                _llmReport = llmService.GenerateMarketSummary(_asset, regime, prediction, isUp, isUp, isUp);
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
            
            MiniAppController.OhlcCandle[]? higherOhlc = null;
            if (_higherTf != null)
            {
                try
                {
                    higherOhlc = await _handler._fetcher.FetchOhlcWithFallbackAsync(_symbol, _higherTf, _asset);
                }
                catch (Exception ex)
                {
                    BotLogger.Warn($"[Analysis] Failed to fetch higher TF OHLC candles: {ex.Message}");
                }
            }
            
            if (_higherResultData.HasValue && higherOhlc != null)
            {
                var htfSmcResult = SmcEngine.AnalyzeSmcStructure(_asset, _higherTf ?? "", higherOhlc, _higherResultData.Value.prices[^1]);
                var mtfValidation = SmcEngine.ValidateMtfSmcAlignment(_smcResult, htfSmcResult);
                _conflictPenalty *= mtfValidation.ConfluenceMultiplier;
                BotLogger.Info($"[MTF SMC Validation] Alignment: {mtfValidation.AlignmentStatus} | Multiplier={mtfValidation.ConfluenceMultiplier:F2}x");
            }

            var (hAdx, hPdi, hMdi) = higherOhlc != null ? _handler._mathEngine.ComputeTrueAdx(_asset, _higherTf ?? "", higherOhlc) : (20.0, 0.0, 0.0);
            double hAtr = higherOhlc != null ? _handler._mathEngine.ComputeAtr(_asset, _higherTf ?? "", higherOhlc) : 0;
            var higherResult = _handler._marketAnalyzer.ScoreTimeframe(_asset, _higherTf ?? "", _higherResultData.Value.prices, _higherResultData.Value.volumes ?? Array.Empty<double>(), candles: higherOhlc, adxOverride: hAdx, atrOverride: hAtr, isForex: _isForex);
            
            _conflictPenalty *= GetMarketAnalysisQueryHandler.MfConflictPenalty(_mainResult, higherResult);
        }
    }

    private async Task<object> BuildFinalConsensusAsync()
    {
        bool isSubMinute = _timeframe.ToLower().StartsWith("s");
        
        // Construct Signals for the Confluence Matrix
        var taSignal = new TaSignal(_mainResult.score, _mainResult.confidence, _mainResult.rsiVal, _mainResult.emaVal, _mainResult.volStrengthVal, _mainAtr);
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

        string finalDirection = consensus.FinalDirection;
        int finalProbability = consensus.Probability;
        
        int timeframeSec = _handler._fetcher.TimeframeSeconds(_timeframe);
        double volRatio = _handler._marketAnalyzer.CalculateVolatilityRatio(_mainPrices);
        var timeoutResult = _handler._timeoutEngine.CalculateTimeout(_asset, _timeframe, _mainAtr, volRatio, _smcResult);
        
        // --- PRODUCTION KILL SWITCH (Pre-Simulation) ---
        if (_wfResult.IsCooloffActive)
        {
            finalDirection = "NEUTRAL";
            finalProbability = 0;
            BotLogger.Warn($"[KillSwitch] Blocked trade for {_asset} {_timeframe} due to WFE Cooloff.");
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
                ["SMC"] = smcSignal.SweepDirection.Contains("BULLISH") ? "BUY" : "PUT", ["ORDERFLOW"] = orderFlowDir,
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
            llmReport = _llmReport
        };
    }
}
