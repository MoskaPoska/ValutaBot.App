using MediatR;
using ValutaBot.MiniApp.CQRS.Queries;
using System.Threading;
using System.Threading.Tasks;

namespace ValutaBot.MiniApp.CQRS.Handlers;

public class GetMarketAnalysisQueryHandler : IRequestHandler<GetMarketAnalysisQuery, object>
{
    internal readonly ITechnicalAnalysisEngine _taEngine;
    internal readonly IWalkForwardValidationEngine _wfEngine;
    internal readonly IConfluenceMatrixEngine _cmEngine;
    internal readonly IAdaptiveExpiryEngine _aeEngine;
    internal readonly MarketDataFetcher _fetcher;

    public GetMarketAnalysisQueryHandler(
        ITechnicalAnalysisEngine taEngine, 
        MarketDataFetcher fetcher, 
        IWalkForwardValidationEngine wfEngine, 
        IConfluenceMatrixEngine cmEngine, 
        IAdaptiveExpiryEngine aeEngine)
    {
        _wfEngine = wfEngine;
        _cmEngine = cmEngine;
        _aeEngine = aeEngine;
        _taEngine = taEngine;
        _fetcher = fetcher;
    }

    internal static double MfConflictPenalty((double score, double conf, double rsi, double ema, double vol, double atr) main,
                                             (double score, double conf, double rsi, double ema, double vol, double atr) higher)
    {
        int mainDir = main.score > 0.05 ? 1 : main.score < -0.05 ? -1 : 0;
        int higherDir = higher.score > 0.05 ? 1 : higher.score < -0.05 ? -1 : 0;
        if (mainDir != 0 && higherDir != 0 && mainDir != higherDir)
            return 0.7; // 30% penalty for active opposing trends
        return 1.0;
    }

    public async Task<object> Handle(GetMarketAnalysisQuery request, CancellationToken cancellationToken)
    {
        var context = new MarketAnalysisContext(this, request.Asset, request.Timeframe);
        return await context.ExecuteAnalysisAsync();
    }

    public ITimeframeAnalyzer GetAnalyzer(string timeframe)
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
