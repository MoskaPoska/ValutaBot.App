using MediatR;
using ValutaBot.MiniApp.CQRS.Queries;
using System.Threading;
using System.Threading.Tasks;
using ValutaBot.MiniApp.Features.MarketAnalysis;

namespace ValutaBot.MiniApp.CQRS.Handlers;

public class GetMarketAnalysisQueryHandler : IRequestHandler<GetMarketAnalysisQuery, object>
{
    private readonly IMarketAnalysisOrchestrator _orchestrator;

    public GetMarketAnalysisQueryHandler(IMarketAnalysisOrchestrator orchestrator)
    {
        _orchestrator = orchestrator;
    }

    public async Task<object> Handle(GetMarketAnalysisQuery request, CancellationToken cancellationToken)
    {
        return await _orchestrator.ExecuteAnalysisAsync(request.Asset, request.Timeframe);
    }
}
