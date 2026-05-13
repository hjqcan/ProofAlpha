using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Autotrade.MarketData.Application.Contract.Tape;
using Autotrade.Trading.Domain.Shared.Enums;

namespace Autotrade.Strategy.Application.Strategies.DualLeg;

public sealed record DualLegArbitrageReplayRequest(
    string MarketId,
    string YesTokenId,
    string NoTokenId,
    decimal Quantity,
    decimal MinOrderQuantity,
    decimal MaxNotionalUsdc,
    decimal PairCostThreshold,
    decimal MaxSlippage,
    decimal FeeRateBps,
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    DateTimeOffset AsOfUtc,
    TimeSpan MaxQuoteAge);

public sealed record DualLegReplayFill(
    string TokenId,
    OutcomeSide Outcome,
    OrderSide Side,
    decimal Quantity,
    decimal AveragePrice,
    decimal SlippageAdjustedAveragePrice,
    decimal NotionalUsdc,
    decimal SlippageAdjustedNotionalUsdc,
    int LevelsConsumed);

public sealed record DualLegArbitrageReplayResult(
    string ReplaySeed,
    string FillModelVersion,
    bool Accepted,
    string Status,
    DateTimeOffset? CandidateTimestampUtc,
    decimal Quantity,
    decimal RawPairCost,
    decimal SlippageAdjustedPairCost,
    decimal FeePerUnit,
    decimal EstimatedFeesUsdc,
    decimal NetEdgeUsdc,
    IReadOnlyList<DualLegReplayFill> Fills,
    IReadOnlyList<string> RejectionReasons,
    IReadOnlyList<string> CompletenessNotes);

public interface IDualLegArbitrageReplayRunner
{
    Task<DualLegArbitrageReplayResult> RunAsync(
        DualLegArbitrageReplayRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class DualLegArbitrageReplayRunner : IDualLegArbitrageReplayRunner
{
    public const string FillModelVersion = "two-leg-depth-aware-fok-v1";

    private const decimal QuantityTolerance = 0.00000001m;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IMarketReplayReader _replayReader;

    public DualLegArbitrageReplayRunner(IMarketReplayReader replayReader)
    {
        _replayReader = replayReader ?? throw new ArgumentNullException(nameof(replayReader));
    }

    public async Task<DualLegArbitrageReplayResult> RunAsync(
        DualLegArbitrageReplayRequest request,
        CancellationToken cancellationToken = default)
    {
        Validate(request);

        var yesTask = _replayReader.GetReplaySliceAsync(
            new MarketTapeQuery(request.MarketId, request.YesTokenId, request.FromUtc, request.ToUtc, request.AsOfUtc),
            cancellationToken);
        var noTask = _replayReader.GetReplaySliceAsync(
            new MarketTapeQuery(request.MarketId, request.NoTokenId, request.FromUtc, request.ToUtc, request.AsOfUtc),
            cancellationToken);

        await Task.WhenAll(yesTask, noTask).ConfigureAwait(false);

        var yesReplay = await yesTask.ConfigureAwait(false);
        var noReplay = await noTask.ConfigureAwait(false);
        var notes = yesReplay.CompletenessNotes
            .Concat(noReplay.CompletenessNotes)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var yesBooks = BuildBookStates(yesReplay, request.YesTokenId, notes);
        var noBooks = BuildBookStates(noReplay, request.NoTokenId, notes);
        var candidateTimes = yesBooks
            .Select(book => book.TimestampUtc)
            .Concat(noBooks.Select(book => book.TimestampUtc))
            .Distinct()
            .OrderBy(timestamp => timestamp)
            .ToArray();

        var rejectionReasons = new List<string>();
        if (candidateTimes.Length == 0)
        {
            rejectionReasons.Add("No replay quotes were available for either leg.");
            return Rejected(request, "no_replay_quotes", notes, rejectionReasons);
        }

        foreach (var timestamp in candidateTimes)
        {
            var yesBook = LatestAtOrBefore(yesBooks, timestamp);
            var noBook = LatestAtOrBefore(noBooks, timestamp);
            if (yesBook is null || noBook is null)
            {
                rejectionReasons.Add($"Missing one leg at {timestamp:O}.");
                continue;
            }

            if (timestamp - yesBook.TimestampUtc > request.MaxQuoteAge ||
                timestamp - noBook.TimestampUtc > request.MaxQuoteAge)
            {
                rejectionReasons.Add($"Quote age exceeded at {timestamp:O}.");
                continue;
            }

            var quantity = FindExecutableQuantity(yesBook, noBook, request);
            if (quantity < request.MinOrderQuantity)
            {
                rejectionReasons.Add($"Two-leg executable quantity below min at {timestamp:O}.");
                continue;
            }

            var yesFill = TryBuildFill(yesBook, OutcomeSide.Yes, quantity, request.MaxSlippage);
            var noFill = TryBuildFill(noBook, OutcomeSide.No, quantity, request.MaxSlippage);
            if (yesFill is null || noFill is null)
            {
                rejectionReasons.Add($"Depth could not fill both legs at {timestamp:O}.");
                continue;
            }

            var adjustedNotional = yesFill.SlippageAdjustedNotionalUsdc + noFill.SlippageAdjustedNotionalUsdc;
            var rawNotional = yesFill.NotionalUsdc + noFill.NotionalUsdc;
            var estimatedFees = adjustedNotional * request.FeeRateBps / 10000m;
            var payout = quantity;
            var netEdge = payout - adjustedNotional - estimatedFees;
            var rawPairCost = rawNotional / quantity;
            var adjustedPairCost = adjustedNotional / quantity;
            var feePerUnit = estimatedFees / quantity;

            if (adjustedPairCost + feePerUnit >= request.PairCostThreshold)
            {
                rejectionReasons.Add(
                    $"Pair cost after slippage and fees {adjustedPairCost + feePerUnit:0.########} >= threshold {request.PairCostThreshold:0.########} at {timestamp:O}.");
                continue;
            }

            if (netEdge <= 0m)
            {
                rejectionReasons.Add($"Net edge was not positive at {timestamp:O}.");
                continue;
            }

            return new DualLegArbitrageReplayResult(
                BuildReplaySeed(request),
                FillModelVersion,
                Accepted: true,
                Status: "accepted",
                timestamp,
                quantity,
                rawPairCost,
                adjustedPairCost,
                feePerUnit,
                estimatedFees,
                netEdge,
                new[] { yesFill, noFill },
                Array.Empty<string>(),
                notes);
        }

        return Rejected(request, "no_profitable_two_leg_fill", notes, rejectionReasons);
    }

    private static IReadOnlyList<BookState> BuildBookStates(
        MarketTapeReplaySlice replay,
        string tokenId,
        List<string> notes)
    {
        var depthStates = replay.DepthSnapshots
            .Where(snapshot => string.Equals(snapshot.TokenId, tokenId, StringComparison.OrdinalIgnoreCase))
            .Where(snapshot => snapshot.Asks.Any(level => !level.IsBid && level.Price > 0m && level.Size > 0m))
            .OrderBy(snapshot => snapshot.TimestampUtc)
            .Select(snapshot => new BookState(
                tokenId,
                snapshot.TimestampUtc,
                snapshot.Asks
                    .Where(level => !level.IsBid && level.Price > 0m && level.Size > 0m)
                    .OrderBy(level => level.Price)
                    .ToArray(),
                "depth"))
            .ToArray();

        if (depthStates.Length > 0)
        {
            return depthStates;
        }

        var topStates = replay.TopTicks
            .Where(tick => string.Equals(tick.TokenId, tokenId, StringComparison.OrdinalIgnoreCase))
            .Where(tick => tick.BestAskPrice is > 0m && tick.BestAskSize is > 0m)
            .OrderBy(tick => tick.TimestampUtc)
            .Select(tick => new BookState(
                tokenId,
                tick.TimestampUtc,
                new[]
                {
                    new OrderBookDepthLevelDto(tick.BestAskPrice!.Value, tick.BestAskSize!.Value, IsBid: false)
                },
                "top-of-book"))
            .ToArray();

        if (topStates.Length > 0)
        {
            notes.Add($"Depth snapshots were missing for {tokenId}; replay degraded to top-of-book ask size.");
        }

        return topStates;
    }

    private static BookState? LatestAtOrBefore(IReadOnlyList<BookState> books, DateTimeOffset timestamp)
        => books.LastOrDefault(book => book.TimestampUtc <= timestamp);

    private static decimal FindExecutableQuantity(
        BookState yesBook,
        BookState noBook,
        DualLegArbitrageReplayRequest request)
    {
        var maxVisibleQuantity = Math.Min(
            request.Quantity,
            Math.Min(SumSize(yesBook.Asks), SumSize(noBook.Asks)));

        if (maxVisibleQuantity < request.MinOrderQuantity)
        {
            return 0m;
        }

        var fullYesFill = TryBuildFill(yesBook, OutcomeSide.Yes, maxVisibleQuantity, request.MaxSlippage);
        var fullNoFill = TryBuildFill(noBook, OutcomeSide.No, maxVisibleQuantity, request.MaxSlippage);
        if (fullYesFill is not null &&
            fullNoFill is not null &&
            fullYesFill.SlippageAdjustedNotionalUsdc + fullNoFill.SlippageAdjustedNotionalUsdc <= request.MaxNotionalUsdc)
        {
            return maxVisibleQuantity;
        }

        var low = 0m;
        var high = maxVisibleQuantity;
        for (var i = 0; i < 64; i++)
        {
            var mid = (low + high) / 2m;
            var yesFill = TryBuildFill(yesBook, OutcomeSide.Yes, mid, request.MaxSlippage);
            var noFill = TryBuildFill(noBook, OutcomeSide.No, mid, request.MaxSlippage);
            if (yesFill is null || noFill is null)
            {
                high = mid;
                continue;
            }

            var notional = yesFill.SlippageAdjustedNotionalUsdc + noFill.SlippageAdjustedNotionalUsdc;
            if (notional <= request.MaxNotionalUsdc)
            {
                low = mid;
            }
            else
            {
                high = mid;
            }
        }

        return Math.Round(low, 8, MidpointRounding.ToZero);
    }

    private static DualLegReplayFill? TryBuildFill(
        BookState book,
        OutcomeSide outcome,
        decimal quantity,
        decimal maxSlippage)
    {
        if (quantity <= QuantityTolerance)
        {
            return null;
        }

        var remaining = quantity;
        var rawNotional = 0m;
        var adjustedNotional = 0m;
        var levelsConsumed = 0;
        foreach (var level in book.Asks)
        {
            var take = Math.Min(remaining, level.Size);
            if (take <= 0m)
            {
                continue;
            }

            levelsConsumed++;
            rawNotional += level.Price * take;
            adjustedNotional += ApplySlippage(level.Price, maxSlippage) * take;
            remaining -= take;
            if (remaining <= QuantityTolerance)
            {
                break;
            }
        }

        if (remaining > QuantityTolerance)
        {
            return null;
        }

        return new DualLegReplayFill(
            book.TokenId,
            outcome,
            OrderSide.Buy,
            quantity,
            rawNotional / quantity,
            adjustedNotional / quantity,
            rawNotional,
            adjustedNotional,
            levelsConsumed);
    }

    private static decimal ApplySlippage(decimal price, decimal maxSlippage)
        => Math.Min(0.99m, price * (1m + maxSlippage));

    private static decimal SumSize(IEnumerable<OrderBookDepthLevelDto> levels)
        => levels.Where(level => !level.IsBid && level.Price > 0m && level.Size > 0m)
            .Sum(level => level.Size);

    private static DualLegArbitrageReplayResult Rejected(
        DualLegArbitrageReplayRequest request,
        string status,
        IReadOnlyList<string> notes,
        IReadOnlyList<string> rejectionReasons)
        => new(
            BuildReplaySeed(request),
            FillModelVersion,
            Accepted: false,
            status,
            CandidateTimestampUtc: null,
            Quantity: 0m,
            RawPairCost: 0m,
            SlippageAdjustedPairCost: 0m,
            FeePerUnit: 0m,
            EstimatedFeesUsdc: 0m,
            NetEdgeUsdc: 0m,
            Array.Empty<DualLegReplayFill>(),
            rejectionReasons,
            notes);

    private static void Validate(DualLegArbitrageReplayRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.MarketId) ||
            string.IsNullOrWhiteSpace(request.YesTokenId) ||
            string.IsNullOrWhiteSpace(request.NoTokenId))
        {
            throw new ArgumentException("MarketId and both token ids are required.", nameof(request));
        }

        if (string.Equals(request.YesTokenId, request.NoTokenId, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("YesTokenId and NoTokenId must be different.", nameof(request));
        }

        if (request.Quantity <= 0m ||
            request.MinOrderQuantity <= 0m ||
            request.MaxNotionalUsdc <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Quantity, MinOrderQuantity, and MaxNotionalUsdc must be positive.");
        }

        if (request.MinOrderQuantity > request.Quantity)
        {
            throw new ArgumentException("MinOrderQuantity cannot exceed Quantity.", nameof(request));
        }

        if (request.PairCostThreshold <= 0m || request.PairCostThreshold > 1m)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "PairCostThreshold must be in (0, 1].");
        }

        if (request.MaxSlippage < 0m || request.FeeRateBps < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "MaxSlippage and FeeRateBps cannot be negative.");
        }

        if (request.FromUtc > request.ToUtc || request.AsOfUtc < request.FromUtc)
        {
            throw new ArgumentException("Replay time range is invalid.", nameof(request));
        }

        if (request.MaxQuoteAge <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "MaxQuoteAge must be positive.");
        }
    }

    private static string BuildReplaySeed(DualLegArbitrageReplayRequest request)
    {
        var json = JsonSerializer.Serialize(request, JsonOptions);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
    }

    private sealed record BookState(
        string TokenId,
        DateTimeOffset TimestampUtc,
        IReadOnlyList<OrderBookDepthLevelDto> Asks,
        string Source);
}
