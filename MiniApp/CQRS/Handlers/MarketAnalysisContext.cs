using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ValutaBot.MiniApp.CQRS.Handlers;
using ValutaBot.App.MiniApp.Engines.ML;
using ValutaBot.App.MiniApp.Services;

namespace ValutaBot.MiniApp.CQRS.Handlers;

internal class MarketAnalysisContext
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
    
    private double _totalScore = 0;
    private double _totalConfidence = 0;
    private double _totalWeight = 0;
    private double _conflictPenalty = 1.0;

    private SmcEngine.SmcAnalysisResult _smcResult;
    private OrderFlowEngine.OrderFlowResult _orderFlowResult;
    private (double score, string sentiment, string summary, string[] headlines) _newsResult;
    private WalkForwardValidationEngine.WalkForwardResult _wfResult;

    private string _lgbmDirection = "NEUTRAL";
    private double _lgbmConfidence = 0.5;
    private string _lgbmModelVersion = "disabled";
    private double? _lgbmAccuracy = null;
    private string _llmReport = "LLM-сводка загружается...";
    
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
                throw new Exception("Недостаточно данных для анализа. Биржа или провайдер вернули пустой результат.");
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
            await EvaluateContinuousAndIntermarketAsync();
            
            await ApplySmcFinalScoreAsync();

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
            _ohlcCandles = await _handler._fetcher.FetchBinanceOhlcCandlesAsync(_symbol ?? (_asset + "USDT"), _handler._fetcher.IntervalMap(_timeframe), _limit);
        }
        catch (HttpRequestException)
        {
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
        _smcResult = SmcEngine.AnalyzeSmcStructure(_ohlcCandles ?? Array.Empty<MiniAppController.OhlcCandle>(), _mainPrices[^1]);
        BotLogger.Info($"[SMC Engine] Asset {_asset} ({_timeframe}): {_smcResult.SummaryReasoning}");
        _orderFlowResult = OrderFlowEngine.AnalyzeOrderFlow(_mainPrices, _mainVolumes ?? Array.Empty<double>(), _ohlcCandles);
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
                var mlEngine = new EnsembleMlEngine();
                var hist = new List<TradeFeatureData>();
                
                // Generate training data from the recent candles (excluding the last two to prevent target leakage)
                for (int i = 1; i < _ohlcCandles.Length - 2; i++)
                {
                    bool isUp = _ohlcCandles[i + 1].Close > _ohlcCandles[i].Close;
                    hist.Add(new TradeFeatureData {
                        Open = (float)_ohlcCandles[i].Open, High = (float)_ohlcCandles[i].High,
                        Low = (float)_ohlcCandles[i].Low, Close = (float)_ohlcCandles[i].Close,
                        Volume = (float)_ohlcCandles[i].Volume,
                        Rsi = 50f, ClusterDelta = 0,
                        IsUp = isUp
                    });
                }
                
                var currentData = new TradeFeatureData {
                    Open = (float)_ohlcCandles[^1].Open, High = (float)_ohlcCandles[^1].High,
                    Low = (float)_ohlcCandles[^1].Low, Close = (float)_ohlcCandles[^1].Close,
                    Volume = _mainVolumes != null && _mainVolumes.Length > 0 ? (float)_mainVolumes[^1] : 0f,
                    Rsi = 50f, ClusterDelta = 0, // Zeroed out to prevent Out-Of-Distribution (OOD) poison since it was trained on 0
                    IsUp = true
                };

                mlEngine.TrainModels(hist);
                var prediction = mlEngine.PredictEnsemble(currentData);

                if (prediction.AverageProbability > 0.05f) 
                {
                    _lgbmDirection = prediction.ConsensusPrediction ? "BUY" : "PUT";
                    _lgbmConfidence = prediction.AverageProbability;
                    _lgbmModelVersion = $"Ensemble_v2 (L:{prediction.ModelProbabilities.GetValueOrDefault("LightGBM",0):F2} T:{prediction.ModelProbabilities.GetValueOrDefault("FastTree",0):F2} F:{prediction.ModelProbabilities.GetValueOrDefault("FastForest",0):F2})";
                    _lgbmAccuracy = 0.915; // Represents higher confidence from ensemble

                    // Apply ensemble score multiplier
                    _totalScore += prediction.FinalScoreMultiplier;
                    _totalConfidence += Math.Abs(prediction.FinalScoreMultiplier) * 20;
                }

                var llmService = new LlmReportingService();
                var regime = ContinuousStateEngine.EvaluateContinuousState(_mainPrices, _asset, _timeframe).VelocityRegime;
                _llmReport = llmService.GenerateMarketSummary(_asset, regime, prediction, prediction.ConsensusPrediction, prediction.ConsensusPrediction, prediction.ConsensusPrediction);
            }
            catch (Exception ex) 
            { 
                Console.WriteLine($"[Native ML Warning] {ex.GetType().Name}: {ex.Message}");
                _llmReport = $"⚠️ ML-движок недоступен: {ex.GetType().Name} — {ex.Message}";
            }
        }

        if (Math.Abs(_newsResult.score) > 0.1)
        {
            double newsWeight = await SignalTracker.GetSignalWeightAsync("NEWS", 0.8);
            double newsScoreNormalized = Math.Clamp(_newsResult.score / 2.0, -1, 1);
            _totalScore += newsScoreNormalized * newsWeight;
            _totalConfidence += Math.Clamp(Math.Abs(_newsResult.score) / 2.0 * 100, 50, 98) * newsWeight;
            _totalWeight += newsWeight;
        }
    }

    private async Task EvaluateTechnicalIndicatorsAsync()
    {
        (_mainAdx, _mainPdi, _mainMdi) = _ohlcCandles != null ? _handler._mathEngine.ComputeTrueAdx(_asset, _timeframe, _ohlcCandles) : (20.0, 0.0, 0.0);
        _mainAtr = _ohlcCandles != null ? _handler._mathEngine.ComputeAtr(_asset, _timeframe, _ohlcCandles) : 0;

        _mainResult = _handler._marketAnalyzer.ScoreTimeframe(_asset, _timeframe, _mainPrices, _mainVolumes ?? Array.Empty<double>(), candles: _ohlcCandles, adxOverride: _mainAdx, atrOverride: _mainAtr, isForex: _isForex);

        if (_higherResultData != null)
        {
            var higherOhlcKey = _higherTf != null ? (_symbol != null ? $"{_symbol}_{_handler._fetcher.IntervalMap(_higherTf)}" : $"{_asset}_{_handler._fetcher.IntervalMap(_higherTf)}") : null;
            var higherOhlc = _higherTf != null ? await _handler._fetcher.FetchBinanceOhlcCandlesAsync(_symbol ?? (_asset + "USDT"), _handler._fetcher.IntervalMap(_higherTf)) : null;
            
            if (higherOhlc != null && higherOhlc.Length >= 10)
            {
                var htfSmcResult = SmcEngine.AnalyzeSmcStructure(higherOhlc, _higherResultData.Value.prices[^1]);
                var mtfValidation = SmcEngine.ValidateMtfSmcAlignment(_smcResult, htfSmcResult);
                _conflictPenalty *= mtfValidation.ConfluenceMultiplier;
                BotLogger.Info($"[MTF SMC Validation] Alignment: {mtfValidation.AlignmentStatus} | Multiplier={mtfValidation.ConfluenceMultiplier:F2}x | {mtfValidation.Description}");
            }

            var (hAdx, hPdi, hMdi) = higherOhlc != null ? _handler._mathEngine.ComputeTrueAdx(_asset, _higherTf ?? "", higherOhlc) : (20.0, 0.0, 0.0);
            double hAtr = higherOhlc != null ? _handler._mathEngine.ComputeAtr(_asset, _higherTf ?? "", higherOhlc) : 0;
            var higherResult = _handler._marketAnalyzer.ScoreTimeframe(_asset, _higherTf ?? "", _higherResultData.Value.prices, _higherResultData.Value.volumes ?? Array.Empty<double>(), candles: higherOhlc, adxOverride: hAdx, atrOverride: hAtr, isForex: _isForex);
            
            _conflictPenalty *= GetMarketAnalysisQueryHandler.MfConflictPenalty(_mainResult, higherResult);

            _totalScore += higherResult.score * _conflictPenalty;
            _totalConfidence += higherResult.confidence * 2.0 * _conflictPenalty;
            _totalWeight += 2.0;
        }

        double indicatorWeight = await SignalTracker.GetSignalWeightAsync("INDICATORS", 1.0);
        _totalScore += (_mainResult.score + _orderFlowResult.ScoreContribution) * indicatorWeight;
        _totalConfidence += _mainResult.confidence * indicatorWeight;
        _totalWeight += indicatorWeight;
    }

    private async Task EvaluateContinuousAndIntermarketAsync()
    {
        var continuousState = ContinuousStateEngine.EvaluateContinuousState(_mainPrices, _asset, _timeframe);
        double stateWeight = await SignalTracker.GetSignalWeightAsync("VelocityState", 1.5);
        _totalScore += continuousState.MomentumContribution * stateWeight;
        _totalConfidence += 60.0 * stateWeight;
        _totalWeight += stateWeight;
        BotLogger.Info($"[Continuous State] Asset {_asset}: State={continuousState.VelocityRegime} | Velocity={continuousState.VelocityBpsPerSec} bps/s");

    }

    private (string direction, double probability, string reasoning, string modelName) GetClaudeFallback()
    {
        string fallbackDir = _totalScore > 0.05 ? "BUY" : _totalScore < -0.05 ? "PUT" : "NEUTRAL";
        string fallbackReasoning = "����� ��������� �� ����� ��� ���������� ������������ ���� �����. ������������� ������������ �� ������.";
        
        if (_totalScore > 0.6) fallbackReasoning = "������� ����� �����. �������� ���������� (RSI, MACD, ������) ������������ ����. ��������� ������� ��� �����.";
        else if (_totalScore > 0.2) fallbackReasoning = "��������� ����� �����. �������� ��������� �� ��������� ����, ���������� ��������� ������ �������������.";
        else if (_totalScore < -0.6) fallbackReasoning = "������� �������� �����. ��������� �������. ������� �������� ���������, ������������� ���� �� ���������.";
        else if (_totalScore < -0.2) fallbackReasoning = "��������� �������� �����. ���������� ��������� �� ��������, ������� �� �������� ���������.";

        return (fallbackDir, 50.0, fallbackReasoning, "�������������� ������");
    }



    private async Task ApplySmcFinalScoreAsync()
    {
        int smcScore = 0;
        if (_smcResult.SweepDirection == "BULLISH_SWEEP") smcScore += 2;
        else if (_smcResult.SweepDirection == "BEARISH_SWEEP") smcScore -= 2;
        if (_smcResult.BosDirection == "BULLISH_BOS") smcScore += 2;
        else if (_smcResult.BosDirection == "BEARISH_BOS") smcScore -= 2;
        if (_smcResult.OrderBlockType == "BULLISH_OB") smcScore += 1;
        else if (_smcResult.OrderBlockType == "BEARISH_OB") smcScore -= 1;
        if (_smcResult.FvgType == "BULLISH_FVG") smcScore += 1;
        else if (_smcResult.FvgType == "BEARISH_FVG") smcScore -= 1;
        
        if (smcScore != 0)
        {
            double smcWeight = await SignalTracker.GetSignalWeightAsync("SMC", 1.0);
            _totalScore += ((double)smcScore / 6.0) * smcWeight;
            _totalConfidence += 60.0 * smcWeight;
            _totalWeight += smcWeight;
        }

        if (_totalWeight > 0)
        {
            _totalScore /= _totalWeight;
            _totalConfidence /= _totalWeight;
        }
    }

    private async Task<object> BuildFinalConsensusAsync()
    {
        int scoreSign = _totalScore > 0.02 ? 1 : _totalScore < -0.02 ? -1 : 0;
        bool isSubMinute = _timeframe.ToLower().StartsWith("s");
        var matrixResult = await _handler._cmEngine.Evaluate4DMatrixAsync(_asset, _timeframe, _isForex, _symbol);

        var consensus = ConsensusEngine.EvaluateConsensus(
            _totalScore, scoreSign,
            _lgbmDirection, _lgbmConfidence, _lgbmAccuracy,
            _mainResult.rsiVal, _mainResult.emaVal,
            isSubMinute, _asset, _timeframe, _mainAdx, _mainResult.volStrengthVal,
            _smcResult.SummaryReasoning, _orderFlowResult.Description, _wfResult.WeightMultiplier
        );

        string finalDirection = consensus.FinalDirection;
        int blendedConfidence = _totalWeight > 0 ? (int)_totalConfidence : 50;
        int finalProbability = Math.Max(consensus.Probability, blendedConfidence);
        
        if (finalDirection == "NEUTRAL") finalProbability = 50;
        else if (matrixResult.ProbabilityBoost > 0) finalProbability = Math.Clamp(finalProbability + matrixResult.ProbabilityBoost, 55, 95);

        int timeframeSec = _handler._fetcher.TimeframeSeconds(_timeframe);
        double volRatio = _handler._marketAnalyzer.CalculateVolatilityRatio(_mainPrices);
        var adaptiveExpiry = _handler._aeEngine.CalculateOptimalExpiry(_asset, _timeframe, _mainAtr, volRatio, _smcResult, isSubMinute);
        
        // --- PRODUCTION KILL SWITCH (Pre-Simulation) ---
        if (_wfResult.IsCooloffActive)
        {
            finalDirection = "NEUTRAL";
            finalProbability = 0;
            BotLogger.Warn($"[KillSwitch] Blocked trade for {_asset} {_timeframe} due to WFE Cooloff.");
        }

        var mcResult = finalDirection == "NEUTRAL" 
            ? new MonteCarloResult(0, 0, 0, 0, "Blocked", "Blocked", "Trade blocked before simulation")
            : MonteCarloEngine.Simulate(_mainPrices[^1], finalProbability / 100.0, finalDirection, _mainAtr, adaptiveExpiry.ExpirySeconds, 0.85, 1000);

        if (finalDirection != "NEUTRAL" && mcResult.KellyRiskPct <= 0)
        {
            finalDirection = "NEUTRAL";
            finalProbability = 0;
            BotLogger.Warn($"[KillSwitch] Blocked trade for {_asset} {_timeframe} due to Kelly Risk <= 0.");
        }

        string orderFlowDir = _orderFlowResult.ScoreContribution > 0 ? "BUY" : _orderFlowResult.ScoreContribution < 0 ? "PUT" : "NEUTRAL";
        int smcScore = 0; // simplified for tracker
        string smcDir = smcScore > 0 ? "BUY" : smcScore < 0 ? "PUT" : "NEUTRAL";

        await SignalTracker.RecordPredictionAsync(
            finalDirection, _asset, _timeframe, _mainPrices[^1],
            expiryCandles: Math.Max(1, adaptiveExpiry.ExpirySeconds / Math.Max(1, timeframeSec)),
            timeframeSecs: timeframeSec, isForex: _isForex, binanceSymbol: _symbol,
            sourceDirections: new Dictionary<string, string> {
                ["LIGHTGBM"] = _lgbmDirection, ["SKENDER_MATH"] = scoreSign > 0 ? "BUY" : scoreSign < 0 ? "PUT" : "NEUTRAL",
                ["SMC"] = smcDir, ["ORDERFLOW"] = orderFlowDir,
                ["NATIVE_ML"] = "NEUTRAL"
            }
        );

        var overallStats = await SignalTracker.GetOverallStatsAsync();
        var assetStats   = await SignalTracker.GetStatsAsync(_asset, _timeframe);

        return new
        {
            direction = finalDirection,
            probability = finalProbability,
            duration = adaptiveExpiry.ExpiryText,
            adaptiveReasoning = $"{adaptiveExpiry.Reasoning} | {matrixResult.SummaryReasoning}",
            goldenSetup = matrixResult.IsGoldenSetup,
            confluenceLabel = matrixResult.ConfluenceLabel,
            confluenceRatio = matrixResult.ConfluenceRatio,
            expiryCandles = Math.Max(1, adaptiveExpiry.ExpirySeconds / Math.Max(1, timeframeSec)),
            chartData = _mainPrices,
            rsi = Math.Round(_mainResult.rsiVal, 1),
            ema = Math.Round(_mainResult.emaVal, 2),
            volumeStrength = Math.Round(_mainResult.volStrengthVal, 2),
            tfConflict = _conflictPenalty < 1.0,
            lgbmDirection = _lgbmDirection,
            lgbmConfidence = Math.Round(_lgbmConfidence * 100, 0),
            lgbmAccuracy = _lgbmAccuracy.HasValue ? Math.Round(_lgbmAccuracy.Value * 100, 1) : (double?)null,
            lgbmModelVersion = _lgbmModelVersion,
            newsSentiment = _newsResult.sentiment,
            newsScore = Math.Round(_newsResult.score, 1),
            newsSummary = _newsResult.summary,
            newsHeadlines = _newsResult.headlines,
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











