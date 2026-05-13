using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Autotrade.MarketData.Application.Contract.Tape;

namespace Autotrade.MarketData.Application.Tape;

public sealed class MarketReplayBacktestRunner : IMarketReplayBacktestRunner
{
    public const string FillModelVersion = "top-of-book-degraded-v1";

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
        notes.Add("Backtest used top-of-book degraded fill model; depth-aware promotion remains required for Live gates.");

        var topTicks = replay.TopTicks
            .OrderBy(tick => tick.TimestampUtc)
            .ThenBy(tick => tick.Id)
            .ToArray();
        var entryTick = topTicks.FirstOrDefault(tick =>
            tick.BestAskPrice.HasValue
            && tick.BestAskSize.HasValue
            && tick.BestAskPrice.Value <= request.EntryMaxPrice
            && tick.BestAskSize.Value > 0m);

        if (entryTick is null)
        {
            notes.Add("No entry fill was available inside the replay window.");
            return Empty(request, notes);
        }

        var entryPrice = entryTick.BestAskPrice!.Value;
        var maxQuantityByNotional = request.MaxNotional / entryPrice;
        var quantity = Math.Min(request.Quantity, Math.Min(maxQuantityByNotional, entryTick.BestAskSize!.Value));
        if (quantity <= 0m)
        {
            notes.Add("Entry capacity was zero after quantity, max notional, and visible ask-size limits.");
            return Empty(request, notes);
        }

        var entry = new MarketReplayFill(entryTick.TimestampUtc, entryPrice, quantity, "entry");
        var exitTick = topTicks
            .Where(tick => tick.TimestampUtc > entryTick.TimestampUtc)
            .FirstOrDefault(tick =>
                tick.BestBidPrice.HasValue
                && tick.BestBidSize.HasValue
                && tick.BestBidSize.Value > 0m
                && (tick.BestBidPrice.Value >= request.TakeProfitPrice
                    || tick.BestBidPrice.Value <= request.StopLossPrice));

        MarketReplayFill? exit = null;
        if (exitTick is not null)
        {
            var reason = exitTick.BestBidPrice!.Value >= request.TakeProfitPrice ? "take_profit" : "stop_loss";
            exit = new MarketReplayFill(
                exitTick.TimestampUtc,
                exitTick.BestBidPrice.Value,
                Math.Min(quantity, exitTick.BestBidSize!.Value),
                reason);
        }
        else
        {
            var lastMark = topTicks
                .Where(tick => tick.TimestampUtc > entryTick.TimestampUtc
                    && tick.BestBidPrice.HasValue
                    && tick.BestBidSize.HasValue
                    && tick.BestBidSize.Value > 0m)
                .LastOrDefault();
            if (lastMark is not null)
            {
                exit = new MarketReplayFill(
                    lastMark.TimestampUtc,
                    lastMark.BestBidPrice!.Value,
                    Math.Min(quantity, lastMark.BestBidSize!.Value),
                    "window_end_mark");
                notes.Add("No take-profit or stop-loss exit occurred; result marks at last replay bid.");
            }
            else
            {
                notes.Add("No exit or mark bid was available after entry.");
            }
        }

        var exited = exit is not null;
        var exitQuantity = exit?.Quantity ?? 0m;
        var grossPnl = exited ? (exit!.Price - entry.Price) * exitQuantity : 0m;
        var fees = EstimateFees(entry.Price, quantity, exit?.Price, exitQuantity, request.FeeRateBps);

        return new MarketReplayBacktestResult(
            BuildReplaySeed(request),
            FillModelVersion,
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
        IReadOnlyList<string> notes)
        => new(
            BuildReplaySeed(request),
            FillModelVersion,
            Entered: false,
            Exited: false,
            Entry: null,
            Exit: null,
            GrossPnl: 0m,
            EstimatedFees: 0m,
            NetPnl: 0m,
            CompletenessNotes: notes);

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
}
