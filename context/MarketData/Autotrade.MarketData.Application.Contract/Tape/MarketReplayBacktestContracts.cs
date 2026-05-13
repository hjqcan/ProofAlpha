using Autotrade.Application.Services;

namespace Autotrade.MarketData.Application.Contract.Tape;

public sealed record MarketReplayBacktestRequest(
    string MarketId,
    string TokenId,
    decimal EntryMaxPrice,
    decimal TakeProfitPrice,
    decimal StopLossPrice,
    decimal Quantity,
    decimal MaxNotional,
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    DateTimeOffset AsOfUtc,
    decimal FeeRateBps = 0m);

public sealed record MarketReplayFill(
    DateTimeOffset TimestampUtc,
    decimal Price,
    decimal Quantity,
    string Reason);

public sealed record MarketReplayBacktestResult(
    string ReplaySeed,
    string FillModelVersion,
    bool Entered,
    bool Exited,
    MarketReplayFill? Entry,
    MarketReplayFill? Exit,
    decimal GrossPnl,
    decimal EstimatedFees,
    decimal NetPnl,
    IReadOnlyList<string> CompletenessNotes);

public interface IMarketReplayBacktestRunner : IApplicationService
{
    Task<MarketReplayBacktestResult> RunAsync(
        MarketReplayBacktestRequest request,
        CancellationToken cancellationToken = default);
}
