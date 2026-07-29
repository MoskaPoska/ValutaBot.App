using MediatR;

namespace ValutaBot.MiniApp.CQRS.Queries;

public record GetMarketAnalysisQuery(string Asset, string Timeframe) : IRequest<object>;
