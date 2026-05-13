using Autotrade.Application.Services;

namespace Autotrade.MarketData.Application.Contract.Tape;

public sealed record MarketTapeGapRepairRequest(
    string? MarketId = null,
    IReadOnlyList<string>? TokenIds = null,
    DateTimeOffset? ObservedAtUtc = null,
    int? MaxMarkets = null,
    int? MaxTokensPerMarket = null,
    decimal? MinVolume24h = null,
    decimal? MinLiquidity = null);

public sealed record MarketTapeGapRepairTokenResult(
    string CatalogMarketId,
    string TapeMarketId,
    string TokenId,
    string Status,
    string? SourceSequence,
    DateTimeOffset? TimestampUtc,
    string? Reason);

public sealed record MarketTapeGapRepairResult(
    DateTimeOffset ObservedAtUtc,
    int MarketsExamined,
    int TokensRequested,
    int TokensRecorded,
    int TokensFailed,
    IReadOnlyList<MarketTapeGapRepairTokenResult> Tokens);

public interface IMarketTapeGapRepairService : IApplicationService
{
    Task<MarketTapeGapRepairResult> RepairAsync(
        MarketTapeGapRepairRequest request,
        CancellationToken cancellationToken = default);
}
