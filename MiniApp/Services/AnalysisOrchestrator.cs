using System.Threading.Tasks;
using MediatR;
using ValutaBot.MiniApp.CQRS.Queries;

namespace ValutaBot.MiniApp;

public class AnalysisOrchestrator : IAnalysisOrchestrator
{
    private readonly IMediator _mediator;

    public AnalysisOrchestrator(IMediator mediator)
    {
        _mediator = mediator;
    }

    public async Task<object> ExecuteBinanceAnalysis(string asset, string timeframe)
    {
        return await _mediator.Send(new GetMarketAnalysisQuery(asset, timeframe));
    }
}
