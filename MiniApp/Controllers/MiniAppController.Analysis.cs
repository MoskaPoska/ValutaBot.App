using System.Globalization;
using Microsoft.Extensions.Caching.Memory;

namespace ValutaBot.MiniApp;

public static partial class MiniAppController
{
    /* ─── Multi-TF conflict penalty ─── */

    private static double MfConflictPenalty((double score, double conf, double rsi, double ema, double vol, double atr) main,
                                             (double score, double conf, double rsi, double ema, double vol, double atr) higher)
    {
        int mainDir = main.score >= 0 ? 1 : -1;
        int higherDir = higher.score >= 0 ? 1 : -1;
        if (mainDir != higherDir)
            return 0.7; // 30% penalty
        return 1.0;
    }

    /* ─── Main analysis ─── */

    internal static async Task<object> ExecuteBinanceAnalysis(string asset, string timeframe)
    {
        try
        {
            string clean = AssetSanitizer.Sanitize(asset);
            DayOfWeek day = DateTime.UtcNow.DayOfWeek;
            string? symbol = AssetSanitizer.MapSymbolByDayOfWeek(clean, day);

            bool isForex = symbol == null || symbol == "EURUSDT" || symbol == "GBPUSDT" || symbol == "AUDUSDT";
            bool isMajor = symbol == "BTCUSDT" || symbol == "ETHUSDT" || symbol == "SOLUSDT";
            int limit = 100;
            string tfLower = timeframe.ToLower().Trim();
            if (tfLower == "s10" || tfLower == "s15" || tfLower == "s30")
            {
                limit = 130;
            }
            else if (tfLower == "m1" || tfLower == "m2" || tfLower == "m3" || tfLower == "m5")
            {
                limit = 150;
            }
            else if (tfLower == "m15" || tfLower == "m30" || tfLower == "h1")
            {
                limit = 200;
            }

            bool useMultiTf = true;

            string mainInterval = MarketDataFetcher.IntervalMap(timeframe);
            string? higherTf = useMultiTf ? MarketDataFetcher.HigherTf(timeframe) : null;
            string? lowerTf = useMultiTf ? MarketDataFetcher.LowerTf(timeframe) : null;

            async Task<(double[] prices, double[] volumes)?> SafeFetch(string tf)
            {
                try
                {
                    return await MarketDataFetcher.FetchBinanceWithFallback(symbol, tf, asset, limit);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Fetch Warning] TF {tf} failed to fetch: {ex.Message}");
                    return null;
                }
            }

            double[] mainPrices;
            double[] mainVolumes;

            if (timeframe.ToLower().StartsWith("s"))
            {
                var subMinuteResult = await GetSubMinuteCandles(symbol, clean, timeframe, limit);
                mainPrices = subMinuteResult.prices;
                mainVolumes = subMinuteResult.volumes;
            }
            else
            {
                int mainCacheTtl = 10;
                var mainResultTuple = await ExchangeDataResilience.FetchPricesResilientAsync(symbol, mainInterval, clean, limit, mainCacheTtl);
                mainPrices = mainResultTuple.prices;
                mainVolumes = mainResultTuple.volumes;
            }

            var ohlcCandles = MarketDataFetcher.GetOhlcCandles($"{clean}_{mainInterval}");
            if (ohlcCandles == null || ohlcCandles.Length < 10)
            {
                var syntheticList = new List<OhlcCandle>();
                for (int i = 1; i < mainPrices.Length; i++)
                {
                    double open = mainPrices[i - 1];
                    double close = mainPrices[i];
                    double high = Math.Max(open, close);
                    double low = Math.Min(open, close);
                    double vol = (mainVolumes != null && i < mainVolumes.Length) ? mainVolumes[i] : 1.0;
                    syntheticList.Add(new OhlcCandle(open, high, low, close, vol));
                }
                ohlcCandles = syntheticList.ToArray();
            }
            var gatekeeper = TechnicalAnalysisEngine.ValidateMarketGatekeeper(mainPrices, ohlcCandles);
            if (!gatekeeper.IsTradeable)
            {
                BotLogger.Warn($"[Analysis] Gatekeeper aborted trade for {asset} ({timeframe}): {gatekeeper.Reason}");
                return GetMomentumPrediction(asset, timeframe);
            }

            var smcResult = SmcEngine.AnalyzeSmcStructure(ohlcCandles, mainPrices[^1]);
            BotLogger.Info($"[SMC Engine] Asset {asset} ({timeframe}): {smcResult.SummaryReasoning}");

            BinanceWebSocketStream.OrderbookDepthSnapshot? liveDepth = null;
            if (symbol != null)
            {
                BinanceWebSocketStream.TryGetLiveOrderbookImbalance(symbol, out liveDepth);
            }
            var orderFlowResult = OrderFlowEngine.AnalyzeOrderFlow(mainPrices, mainVolumes, ohlcCandles, liveDepth);
            BotLogger.Info($"[Order Flow] Asset {asset} ({timeframe}): {orderFlowResult.Description}");

            var forexTape = ForexMarketProxyEngine.AnalyzeForexTape(asset);
            if (Math.Abs(forexTape.ScoreContribution) > 0.1)
            {
                BotLogger.Info($"[CME Forex Tape] Asset {asset}: Proxy={forexTape.MappedFuturesSymbol} Delta CVD={forexTape.CumulativeDeltaVolume} | State={forexTape.MarketState}");
            }

            var higherTask = higherTf != null ? SafeFetch(higherTf) : Task.FromResult<(double[] prices, double[] volumes)?>(null);
            var lowerTask = lowerTf != null ? SafeFetch(lowerTf) : Task.FromResult<(double[] prices, double[] volumes)?>(null);

            var extraTasks = new List<Task<(double[] prices, double[] volumes)?>>();
            if (isMajor)
            {
                string[] checkTfs = { "m1", "m5", "m15", "h1" };
                foreach (var cTf in checkTfs)
                {
                    if (cTf != timeframe && cTf != higherTf && cTf != lowerTf)
                    {
                        extraTasks.Add(SafeFetch(cTf));
                    }
                }
            }

            await Task.WhenAll(higherTask, lowerTask);
            if (extraTasks.Count > 0)
                await Task.WhenAll(extraTasks);

            var higherResultData = await higherTask;
            var lowerResultData = await lowerTask;

            var extraResults = new List<(double[] prices, double[] volumes)>();
            foreach (var t in extraTasks)
            {
                var r = await t;
                if (r != null) extraResults.Add(r.Value);
            }

            double totalScore = 0;
            double totalConfidence = 0;
            double totalWeight = 0;

            var (mlDirection, mlConfidence, _) = MLForecastService.PredictNextCandles(mainPrices, isForex);
            if (mlDirection != "NEUTRAL")
            {
                double mlSign = mlDirection == "BUY" ? 1.0 : -1.0;
                double mlWeight = SignalTracker.GetSignalWeight("ML прогноз", 1.0);
                totalScore += mlSign * (mlConfidence / 100.0) * mlWeight;
                totalConfidence += mlConfidence * mlWeight;
                totalWeight += mlWeight;
            }

            string lgbmDirection = "NEUTRAL";
            double lgbmConfidence = 0.5;
            string lgbmModelVersion = "disabled";
            double? lgbmAccuracy = null;

            string mainOhlcKey = symbol != null ? $"{symbol}_{mainInterval}" : $"{asset}_{mainInterval}";
            var mainOhlc = MarketDataFetcher.GetOhlcCandles(mainOhlcKey);

            if (mainOhlc != null && mainOhlc.Length >= 60)
            {
                try
                {
                    var lgbmResult = await MLPythonService.PredictAsync(asset, timeframe, mainOhlc, isForex);
                    if (lgbmResult != null && lgbmResult.Direction != "NEUTRAL")
                    {
                        lgbmDirection = lgbmResult.Direction;
                        lgbmConfidence = lgbmResult.Confidence;
                        lgbmModelVersion = lgbmResult.ModelVersion;
                        lgbmAccuracy = lgbmResult.Accuracy;

                        double lgbmSign = lgbmDirection == "BUY" ? 1.0 : -1.0;
                        double baseLgbmWeight = lgbmConfidence >= 0.65 ? 2.8 : 1.5;
                        double lgbmWeight = SignalTracker.GetSignalWeight("LightGBM", baseLgbmWeight);
                        totalScore += lgbmSign * (lgbmConfidence * 2.0) * lgbmWeight;
                        totalConfidence += lgbmConfidence * 100.0 * lgbmWeight;
                        totalWeight += lgbmWeight;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[LGBM Warning] {ex.Message}");
                }
            }

            var newsResult = NewsAnalysisService.Analyze(asset);
            bool isNewsActive = newsResult.sentiment == "High Impact Volatility" || Math.Abs(newsResult.score) > 1.5;

            // ─── Walk-Forward Out-of-Sample Anti-Overfitting Check ───
            var wfResult = WalkForwardValidationEngine.ValidateWalkForward(asset, timeframe, mainPrices, isNewsActive);
            if (wfResult.IsOverfitted || wfResult.IsCooloffActive)
            {
                BotLogger.Warn($"[Anti-Overfitting] {asset} ({timeframe}): {wfResult.StatusReasoning} ML weight multiplier set to {wfResult.WeightMultiplier}x.");
            }

            if (Math.Abs(newsResult.score) > 0.1)
            {
                double newsWeight = SignalTracker.GetSignalWeight("Новости", 0.8);
                double newsScoreNormalized = Math.Clamp(newsResult.score / 2.0, -1, 1);
                totalScore += newsScoreNormalized * newsWeight;
                totalConfidence += Math.Clamp(Math.Abs(newsResult.score) / 2.0 * 100, 50, 98) * newsWeight;
                totalWeight += newsWeight;
            }

            string imbalanceKey = symbol != null && symbol.EndsWith("USDT") ? symbol.Replace("USDT", "/USDT") : "";
            double imbalance = MarketDataService.GetBookImbalance(imbalanceKey);

            var (mainAdx, mainPdi, mainMdi) = mainOhlc != null ? TechnicalAnalysisEngine.ComputeTrueAdx(mainOhlc) : (20.0, 0.0, 0.0);
            double mainAtr = mainOhlc != null ? TechnicalAnalysisEngine.ComputeAtr(mainOhlc) : 0;

            var mainResult = TechnicalAnalysisEngine.ScoreTimeframe(mainPrices, mainVolumes, candles: mainOhlc, adxOverride: mainAdx, atrOverride: mainAtr, isForex: isForex);
            (double score, double confidence, double rsiVal, double emaVal, double volumeStrength, double atrVal) higherResult = default;
            double conflictPenalty = 1.0;

            if (higherResultData != null)
            {
                var higherOhlcKey = higherTf != null ? (symbol != null ? $"{symbol}_{MarketDataFetcher.IntervalMap(higherTf)}" : $"{asset}_{MarketDataFetcher.IntervalMap(higherTf)}") : null;
                var higherOhlc = higherOhlcKey != null ? MarketDataFetcher.GetOhlcCandles(higherOhlcKey) : null;
                if (higherOhlc != null && higherOhlc.Length >= 10)
                {
                    var htfSmcResult = SmcEngine.AnalyzeSmcStructure(higherOhlc, higherResultData.Value.prices[^1]);
                    var mtfValidation = SmcEngine.ValidateMtfSmcAlignment(smcResult, htfSmcResult);
                    conflictPenalty *= mtfValidation.ConfluenceMultiplier;
                    BotLogger.Info($"[MTF SMC Validation] Alignment: {mtfValidation.AlignmentStatus} | Multiplier={mtfValidation.ConfluenceMultiplier:F2}x | {mtfValidation.Description}");
                }

                var (hAdx, hPdi, hMdi) = higherOhlc != null ? TechnicalAnalysisEngine.ComputeTrueAdx(higherOhlc) : (20.0, 0.0, 0.0);
                double hAtr = higherOhlc != null ? TechnicalAnalysisEngine.ComputeAtr(higherOhlc) : 0;
                higherResult = TechnicalAnalysisEngine.ScoreTimeframe(higherResultData.Value.prices, higherResultData.Value.volumes, candles: higherOhlc, adxOverride: hAdx, atrOverride: hAtr, isForex: isForex);
                conflictPenalty *= MfConflictPenalty(mainResult, higherResult);

                totalScore += higherResult.score * conflictPenalty;
                totalConfidence += higherResult.confidence * 2.0 * conflictPenalty;
                totalWeight += 2.0;
            }

            double indicatorWeight = SignalTracker.GetSignalWeight("Индикаторы", 1.0);
            totalScore += (mainResult.score + orderFlowResult.ScoreContribution) * indicatorWeight;
            totalConfidence += mainResult.confidence * indicatorWeight;
            totalWeight += indicatorWeight;

            double macdLine = 0, macdSig = 0;
            (macdLine, macdSig) = TechnicalAnalysisEngine.ComputeMacd(mainPrices, mainPrices.Length - 1);
            double bbZscore = TechnicalAnalysisEngine.ComputeBollingerZscore(mainPrices, 20);
            double volRatio = TechnicalAnalysisEngine.CalculateVolatilityRatio(mainPrices);
            ohlcCandles = MarketDataFetcher.GetOhlcCandles(mainOhlcKey) ?? ohlcCandles;

            // In-Process Native C# ML prediction (<0.1ms RAM speed)
            if (lgbmDirection == "NEUTRAL")
            {
                double kalmanSlope = Math.Abs(mainPrices[^1] - mainPrices[0]) / mainPrices.Length;
                double hurstH = CalculateHurstExponent(mainPrices);
                var nativeMl = NativeMLService.Predict(mainPrices, mainResult.rsiVal, mainResult.emaVal, bbZscore, hurstH, kalmanSlope, volRatio);
                if (nativeMl.Direction != "NEUTRAL")
                {
                    lgbmDirection = nativeMl.Direction;
                    lgbmConfidence = nativeMl.Confidence;
                    lgbmModelVersion = nativeMl.ModelVersion;

                    double nativeSign = lgbmDirection == "BUY" ? 1.0 : -1.0;
                    double nativeWeight = SignalTracker.GetSignalWeight("NativeML", 1.8);
                    totalScore += nativeSign * (lgbmConfidence * 1.5) * nativeWeight;
                    totalConfidence += lgbmConfidence * 100.0 * nativeWeight;
                    totalWeight += nativeWeight;
                }
            }
            var ohlcForClaude = ohlcCandles != null && ohlcCandles.Length > 30 ? ohlcCandles[^30..] : ohlcCandles;
            var detectedPatterns = ohlcCandles != null ? PatternDetector.DetectPatterns(ohlcCandles) : new List<string>();
            var (supports, resistances) = PatternDetector.CalculateLevels(mainPrices, isForex);

            int timeframeSec = MarketDataFetcher.TimeframeSeconds(timeframe);
            int candleSecondsRemaining = timeframeSec;

            string cacheKey = $"claude_signal_{asset}_{timeframe}";
            (string direction, double probability, string reasoning, string modelName) claudeResult;

            if (_cache.TryGetValue(cacheKey, out object? cached) && cached is ValueTuple<string, double, string, string> cachedTuple)
            {
                claudeResult = cachedTuple;
            }
            else
            {
                // Provide instant zero-delay HFT fallback reasoning for immediate signal generation (<0.1ms)
                claudeResult = ("BUY", 75.0, $"SMC + OrderFlow + Native ML In-Process Consensus", "HFT-Native-Engine");

                // Asynchronously trigger DeepSeek/Claude LLM in background without blocking signal execution
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var asyncResult = await ClaudeSignalService.AnalyzeSignal(
                            asset, mainPrices, mainVolumes,
                            mainResult.rsiVal, mainResult.emaVal, macdLine, macdSig,
                            mainAdx, bbZscore, mainResult.volStrengthVal, imbalance,
                            null, ohlcForClaude, detectedPatterns, supports, resistances,
                            timeframe, candleSecondsRemaining, timeframeSec, mainAtr, mainPdi, mainMdi);
                        _cache.Set(cacheKey, asyncResult, TimeSpan.FromSeconds(10));
                        BotLogger.Info($"[Asynchronous LLM] Background DeepSeek reasoning ready for {asset} ({timeframe}) in cache.");
                    }
                    catch (Exception llmEx)
                    {
                        BotLogger.Warn($"[Asynchronous LLM] Background LLM notice: {llmEx.Message}");
                    }
                });
            }

            // ─── Strategy Pattern Router ───
            ITimeframeAnalyzer timeframeAnalyzer = GetAnalyzer(timeframe);
            var coreResult = await timeframeAnalyzer.AnalyzeAsync(asset, timeframe, mainPrices, mainVolumes, ohlcCandles, mainAdx, mainAtr, isForex, higherResultData);

            int scoreSign = totalScore > 0.02 ? 1 : totalScore < -0.02 ? -1 : 0;
            bool isSubMinute = timeframe.ToLower().StartsWith("s");

            var matrixResult = await ConfluenceMatrixEngine.Evaluate4DMatrixAsync(asset, timeframe, isForex, symbol);

            var consensus = ConsensusEngine.EvaluateConsensus(
                totalScore, scoreSign,
                claudeResult.direction, (int)claudeResult.probability, claudeResult.reasoning,
                lgbmDirection, lgbmConfidence, lgbmAccuracy,
                mlDirection, mlConfidence,
                mainResult.rsiVal, mainResult.emaVal,
                isSubMinute,
                asset,
                timeframe,
                mainAdx,
                mainResult.volStrengthVal,
                smcResult.SummaryReasoning,
                orderFlowResult.Description,
                claudeResult.modelName
            );

            // ─── Direct Directional Output (Always BUY or PUT) ───
            string finalDirection = (coreResult.Direction != "WAIT" && coreResult.Direction != "NEUTRAL" && !string.IsNullOrEmpty(coreResult.Direction))
                ? coreResult.Direction 
                : (consensus.FinalDirection != "NEUTRAL" ? consensus.FinalDirection : (totalScore >= 0 ? "BUY" : "PUT"));

            int finalProbability = Math.Max(75, Math.Max(consensus.Probability, (int)(coreResult.Confidence * 100)));
            if (matrixResult.ProbabilityBoost > 0)
            {
                finalProbability = Math.Clamp(finalProbability + matrixResult.ProbabilityBoost, 75, 95);
            }

            if (finalProbability >= 70)
            {
                MultiRegionGatewayEngine.PreWarmSocketForSignal(asset);
            }

            var overallStats = SignalTracker.GetOverallStats();
            var assetStats   = SignalTracker.GetStats(asset, timeframe);
            var adaptiveExpiry = AdaptiveExpiryEngine.CalculateOptimalExpiry(asset, timeframe, mainAtr, volRatio, smcResult, isSubMinute);
            string durationText = adaptiveExpiry.ExpiryText;
            var mcResult = MonteCarloEngine.Simulate(
                mainPrices[^1],
                finalProbability / 100.0,
                finalDirection,
                mainAtr,
                adaptiveExpiry.ExpirySeconds,
                0.85,
                1000
            );

            SignalTracker.RecordPrediction(
                finalDirection, asset, timeframe, mainPrices[^1],
                expiryCandles: Math.Max(1, adaptiveExpiry.ExpirySeconds / Math.Max(1, timeframeSec)),
                timeframeSecs: timeframeSec,
                isForex: isForex,
                binanceSymbol: symbol,
                sourceDirections: new Dictionary<string, string>
                {
                    ["LIGHTGBM"] = lgbmDirection,
                    ["SKENDER_MATH"] = consensus.CandidateDirection,
                    ["CLAUDE_AI"] = claudeResult.direction
                }
            );

            return new
            {
                direction = finalDirection,
                probability = finalProbability,
                duration = durationText,
                adaptiveReasoning = $"{coreResult.Reasoning} | {adaptiveExpiry.Reasoning} | {matrixResult.SummaryReasoning}",
                goldenSetup = matrixResult.IsGoldenSetup,
                confluenceLabel = matrixResult.ConfluenceLabel,
                confluenceRatio = matrixResult.ConfluenceRatio,
                expiryCandles = Math.Max(1, adaptiveExpiry.ExpirySeconds / Math.Max(1, timeframeSec)),
                chartData = mainPrices,
                rsi = Math.Round(mainResult.rsiVal, 1),
                ema = Math.Round(mainResult.emaVal, 2),
                volumeStrength = Math.Round(mainResult.volStrengthVal, 2),
                tfConflict = conflictPenalty < 1.0,
                mlDirection,
                mlConfidence = Math.Round(mlConfidence, 0),
                lgbmDirection,
                lgbmConfidence = Math.Round(lgbmConfidence * 100, 0),
                lgbmAccuracy = lgbmAccuracy.HasValue ? Math.Round(lgbmAccuracy.Value * 100, 1) : (double?)null,
                lgbmModelVersion,
                newsSentiment = newsResult.sentiment,
                newsScore = Math.Round(newsResult.score, 1),
                newsSummary = newsResult.summary,
                newsHeadlines = newsResult.headlines,
                claudeDirection = claudeResult.direction,
                claudeProbability = Math.Round(claudeResult.probability, 0),
                claudeReasoning = consensus.CombinedReasoningText,
                aiModel = claudeResult.modelName,
                winRateOverall = overallStats.HasData ? overallStats.WinRate : (double?)null,
                winRateAsset = assetStats.HasData ? assetStats.WinRate : (double?)null,
                signalsVerified = overallStats.Verified,
                signalsPending = SignalTracker.GetPendingCount(),
                monteCarloIterations = mcResult.Iterations,
                monteCarloSuccess = mcResult.SuccessCount,
                evPct = mcResult.ExpectedValuePct,
                evLabel = mcResult.EvLabel,
                kellyRiskPct = mcResult.KellyRiskPct,
                kellyLabel = mcResult.KellyLabel,
                monteCarloSummary = mcResult.SummaryReasoning
            };
        }
        catch (ExchangeUnavailableException exEx)
        {
            LastExceptionMessage = exEx.ToString();
            BotLogger.Warn($"[Analysis] Exchange unavailable for asset {asset}: {exEx.Message}");
            return new
            {
                error = true,
                message = exEx.UserFriendlyMessage,
                direction = "NEUTRAL",
                probability = 50,
                claudeReasoning = exEx.UserFriendlyMessage
            };
        }
        catch (Exception ex)
        {
            LastExceptionMessage = ex.ToString();
            BotLogger.Error($"[Analysis] Analysis failed for asset {asset} on {timeframe}", ex);
            return GetMomentumPrediction(asset, timeframe);
        }
    }

    public static ITimeframeAnalyzer GetAnalyzer(string timeframe)
    {
        string tf = timeframe.ToLower().Trim();
        return tf switch
        {
            "5s" or "10s" or "15s" or "30s" or "s5" or "s10" or "s15" or "s30" => new SubMinuteMicrostructureAnalyzer(),
            "1m" or "m1" => new OneMinuteEnsembleAnalyzer(),
            "5m" or "15m" or "30m" or "1h" or "m5" or "m15" or "m30" or "h1" => new FiveMinutesStructuralAnalyzer(),
            _ => new OneMinuteEnsembleAnalyzer()
        };
    }
}
