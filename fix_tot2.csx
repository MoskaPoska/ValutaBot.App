using System.IO;

var lines = File.ReadAllText("MiniApp/Services/TradeOutcomeTracker.cs");

var initOld = @"    public static void Initialize()
    {
        if (_initialized) return;
        lock (_initLock)
        {
            if (_initialized) return;

            try
            {
                var outcomes = BotDatabase.LoadTradeOutcomes(1000);
                BotLogger.Info($""[TradeOutcomeTracker] Loaded {outcomes.Count} historical outcomes from SQLite DB."");

                foreach (var outcome in outcomes)
                {
                    AutoCalibrationEngine.RecordSourceOutcome(""GLOBAL"", outcome.Asset, outcome.Timeframe, outcome.WasWin);
                }

                _initialized = true;
                BotLogger.Info(""[TradeOutcomeTracker] Online Reinforcement Learning engine initialized successfully."");
            }
            catch (Exception ex)
            {
                BotLogger.Error(""[TradeOutcomeTracker] Failed to initialize trade outcome tracker"", ex);
            }
        }
    }";

var initNew = @"    private static readonly System.Threading.SemaphoreSlim _initSemaphore = new(1, 1);
    public static async Task InitializeAsync()
    {
        if (_initialized) return;
        await _initSemaphore.WaitAsync();
        try
        {
            if (_initialized) return;

            try
            {
                var outcomes = await BotDatabase.LoadTradeOutcomesAsync(1000);
                BotLogger.Info($""[TradeOutcomeTracker] Loaded {outcomes.Count} historical outcomes from PostgreSQL DB."");

                foreach (var outcome in outcomes)
                {
                    AutoCalibrationEngine.RecordSourceOutcome(""GLOBAL"", outcome.Asset, outcome.Timeframe, outcome.WasWin);
                }

                _initialized = true;
                BotLogger.Info(""[TradeOutcomeTracker] Online Reinforcement Learning engine initialized successfully."");
            }
            catch (Exception ex)
            {
                BotLogger.Error(""[TradeOutcomeTracker] Failed to initialize trade outcome tracker"", ex);
            }
        }
        finally
        {
            _initSemaphore.Release();
        }
    }";

lines = lines.Replace(initOld, initNew);
lines = lines.Replace(initOld.Replace("\r\n", "\n"), initNew.Replace("\r\n", "\n"));

File.WriteAllText("MiniApp/Services/TradeOutcomeTracker.cs", lines);
