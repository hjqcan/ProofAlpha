using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Autotrade.MarketData.Application.Contract.Tape;

namespace Autotrade.MarketData.Application.Tape;

public sealed class MarketReplayBacktestRunner : IMarketReplayBacktestRunner
{
    public const string FillModelVersion = TopOfBookFillModelVersion;
    public const string TopOfBookFillModelVersion = "top-of-book-degraded-v1";
    public const string DepthAwareFillModelVersion = "single-leg-depth-aware-v1";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IMarketReplayReader _replayReader;

    public MarketReplayBacktestRunner(IMarketReplayReader replayReader)
    {
        _replayReader = replayReader ?? throw new ArgumentNullException(nameof(replayReader));
    }

    public async Task<MarketReplayBacktestResult> RunAsync(
        MarketReplayBacktestRequest request,
        CancellationToken cancellationToken = default)
    {
        Validate(request);

        var query = new MarketTapeQuery(
            request.MarketId,
            request.TokenId,
            request.FromUtc,
            request.ToUtc,
            request.AsOfUtc);
        var replay = await _replayReader.GetReplaySliceAsync(query, cancellationToken).ConfigureAwait(false);
        var notes = replay.CompletenessNotes.ToList();
        var (books, fillModelVersion) = BuildBookStates(replay, request.TokenId, notes);

        var entryCandidate = books.FirstOrDefault(book =>
            book.Asks.Any(level => level.Price <= request.EntryMaxPrice && level.Size > 0m));
        if (entryCandidate is null)
        {
            notes.Add("No entry fill was available inside the replay window.");
            return Empty(request, fillModelVersion, notes);
        }

        var entry = TryBuildBuyFill(
            entryCandidate,
            request.Quantity,
            request.MaxNotional,
            level => level.Price <= request.EntryMaxPrice,
            "entry");
        if (entry is null)
        {
            notes.Add("Entry capacity was zero after quantity, max notional, visible ask-size, and entry price limits.");
            return Empty(request, fillModelVersion, notes);
        }

        MarketReplayFill? exit = null;
        var exitCandidate = books
            .Where(book => book.TimestampUtc > entryCandidate.TimestampUtc)
            .FirstOrDefault(book =>
            {
                var bestBid = book.Bids.FirstOrDefault();
                return bestBid is not null &&
                    (bestBid.Price >= request.TakeProfitPrice || bestBid.Price <= request.StopLossPrice);
            });

        if (exitCandidate is not null)
        {
            var bestBid = exitCandidate.Bids[0].Price;
            if (bestBid >= request.TakeProfitPrice)
            {
                exit = TryBuildSellFill(
                    exitCandidate,
                    entry.Quantity,
                    level => level.Price >= request.TakeProfitPrice,
                    "take_profit");
            }
            else
            {
                exit = TryBuildSellFill(
                    exitCandidate,
                    entry.Quantity,
                    level => level.Price > 0m,
                    "stop_loss");
            }
        }
        else
        {
            var lastMark = books
                .Where(book => book.TimestampUtc > entryCandidate.TimestampUtc && book.Bids.Count > 0)
                .LastOrDefault();
            if (lastMark is not null)
            {
                exit = TryBuildSellFill(
                    lastMark,
                    entry.Quantity,
                    level => level.Price > 0m,
                    "window_end_mark");
                if (exit is not null)
                {
                    notes.Add("No take-profit or stop-loss exit occurred; result marks at last replay bid.");
                }
            }
            else
            {
                notes.Add("No exit or mark bid was available after entry.");
            }
        }

        var exited = exit is not null;
        var exitQuantity = exit?.Quantity ?? 0m;
        var grossPnl = exited ? (exit!.Price - entry.Price) * exitQuantity : 0m;
        var fees = EstimateFees(entry.Price, entry.Quantity, exit?.Price, exitQuantity, request.FeeRateBps);

        return new MarketReplayBacktestResult(
            BuildReplaySeed(request),
            fillModelVersion,
            Entered: true,
            Exited: exited,
            entry,
            exit,
            grossPnl,
            fees,
            grossPnl - fees,
            notes);
    }

    private static MarketReplayBacktestResult Empty(
        MarketReplayBacktestRequest request,
        string fillModelVersion,
        IReadOnlyList<string> notes)
        => new(
            BuildReplaySeed(request),
            fillModelVersion,
            Entered: false,
            Exited: false,
            Entry: null,
            Exit: null,
            GrossPnl: 0m,
            EstimatedFees: 0m,
            NetPnl: 0m,
            CompletenessNotes: notes);

    private static (IReadOnlyList<BookState> Books, string FillModelVersion) BuildBookStates(
        MarketTapeReplaySlice replay,
        string tokenId,
        List<string> notes)
    {
        var depthStates = replay.DepthSnapshots
            .Where(snapshot => string.Equals(snapshot.TokenId, tokenId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(snapshot => snapshot.TimestampUtc)
            .ThenBy(snapshot => snapshot.Id)
            .Select(snapshot => new BookState(
                snapshot.TimestampUtc,
                snapshot.Bids
                    .Where(level => level.IsBid && level.Price > 0m && level.Size > 0m)
                    .OrderByDescending(level => level.Price)
                    .ToArray(),
                snapshot.Asks
                    .Where(level => !level.IsBid && level.Price > 0m && level.Size > 0m)
                    .OrderBy(level => level.Price)
                    .ToArray()))
            .ToArray();
        if (depthStates.Length > 0)
        {
            return (depthStates, DepthAwareFillModelVersion);
        }

        var topStates = replay.TopTicks
            .Where(tick => string.Equals(tick.TokenId, tokenId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(tick => tick.TimestampUtc)
            .ThenBy(tick => tick.Id)
            .Select(tick => new BookState(
                tick.TimestampUtc,
                tick.BestBidPrice is > 0m && tick.BestBidSize is > 0m
                    ? [new OrderBookDepthLevelDto(tick.BestBidPrice.Value, tick.BestBidSize.Value, IsBid: true)]
                    : Array.Empty<OrderBookDepthLevelDto>(),
                tick.BestAskPrice is > 0m && tick.BestAskSize is > 0m
                    ? [new OrderBookDepthLevelDto(tick.BestAskPrice.Value, tick.BestAskSize.Value, IsBid: false)]
                    : Array.Empty<OrderBookDepthLevelDto>()))
            .ToArray();

        if (topStates.Length > 0)
        {
            notes.Add("Depth snapshots were missing; backtest degraded to top-of-book fill model.");
        }

        return (topStates, TopOfBookFillModelVersion);
    }

    private static MarketReplayFill? TryBuildBuyFill(
        BookState book,
        decimal requestedQuantity,
        decimal maxNotional,
        Func<OrderBookDepthLevelDto, bool> canUseLevel,
        string reason)
    {
        var remainingQuantity = requestedQuantity;
        var remainingNotional = maxNotional;
        var filledQuantity = 0m;
        var filledNotional = 0m;

        foreach (var level in book.Asks.Where(canUseLevel))
        {
            if (level.Price <= 0m)
            {
                continue;
            }

            var take = Math.Min(remainingQuantity, level.Size);
            take = Math.Min(take, remainingNotional / level.Price);
            if (take <= 0m)
            {
                break;
            }

            filledQuantity += take;
            filledNotional += take * level.Price;
            remainingQuantity -= take;
            remainingNotional -= take * level.Price;
            if (remainingQuantity <= 0m || remainingNotional <= 0m)
            {
                break;
            }
        }

        return BuildFill(book.TimestampUtc, filledQuantity, filledNotional, reason);
    }

    private static MarketReplayFill? TryBuildSellFill(
        BookState book,
        decimal requestedQuantity,
        Func<OrderBookDepthLevelDto, bool> canUseLevel,
        string reason)
    {
        var remainingQuantity = requestedQuantity;
        var filledQuantity = 0m;
        var filledNotional = 0m;

        foreach (var level in book.Bids.Where(canUseLevel))
        {
            var take = Math.Min(remainingQuantity, level.Size);
            if (take <= 0m)
            {
                continue;
            }

            filledQuantity += take;
            filledNotional += take * level.Price;
            remainingQuantity -= take;
            if (remainingQuantity <= 0m)
            {
                break;
            }
        }

        return BuildFill(book.TimestampUtc, filledQuantity, filledNotional, reason);
    }

    private static MarketReplayFill? BuildFill(
        DateTimeOffset timestampUtc,
        decimal filledQuantity,
        decimal filledNotional,
        string reason)
        => filledQuantity <= 0m
            ? null
            : new MarketReplayFill(timestampUtc, filledNotional / filledQuantity, filledQuantity, reason);

    private static void Validate(MarketReplayBacktestRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.MarketId))
        {
            throw new ArgumentException("MarketId cannot be empty.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.TokenId))
        {
            throw new ArgumentException("TokenId cannot be empty.", nameof(request));
        }

        if (request.EntryMaxPrice <= 0m || request.TakeProfitPrice <= 0m || request.StopLossPrice <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Prices must be positive.");
        }

        if (request.StopLossPrice >= request.EntryMaxPrice || request.TakeProfitPrice <= request.EntryMaxPrice)
        {
            throw new ArgumentException("StopLossPrice must be below EntryMaxPrice and TakeProfitPrice above it.", nameof(request));
        }

        if (request.Quantity <= 0m || request.MaxNotional <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Quantity and MaxNotional must be positive.");
        }

        if (request.FromUtc > request.ToUtc)
        {
            throw new ArgumentException("FromUtc must be before or equal to ToUtc.", nameof(request));
        }

        if (request.AsOfUtc < request.FromUtc)
        {
            throw new ArgumentException("AsOfUtc must be on or after FromUtc.", nameof(request));
        }

        if (request.FeeRateBps < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "FeeRateBps cannot be negative.");
        }
    }

    private static decimal EstimateFees(
        decimal entryPrice,
        decimal entryQuantity,
        decimal? exitPrice,
        decimal exitQuantity,
        decimal feeRateBps)
    {
        if (feeRateBps <= 0m)
        {
            return 0m;
        }

        var entryFee = entryPrice * entryQuantity * feeRateBps / 10000m;
        var exitFee = (exitPrice ?? 0m) * exitQuantity * feeRateBps / 10000m;
        return entryFee + exitFee;
    }

    private static string BuildReplaySeed(MarketReplayBacktestRequest request)
    {
        var json = JsonSerializer.Serialize(request, JsonOptions);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
    }

    private sealed record BookState(
        DateTimeOffset TimestampUtc,
        IReadOnlyList<OrderBookDepthLevelDto> Bids,
        IReadOnlyList<OrderBookDepthLevelDto> Asks);
}
