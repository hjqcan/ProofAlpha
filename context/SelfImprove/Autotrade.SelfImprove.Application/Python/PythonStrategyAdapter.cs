using System.Text.Json;
using Autotrade.MarketData.Application.Contract.OrderBook;
using Autotrade.Strategy.Application.Contract.Strategies;
using Autotrade.Trading.Application.Contract.Risk;
using Autotrade.Trading.Domain.Shared.Enums;

namespace Autotrade.SelfImprove.Application.Python;

public sealed class PythonStrategyAdapter : ITradingStrategy
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly PythonStrategyManifest _manifest;
    private readonly StrategyContext _context;
    private readonly IPythonStrategyRuntime _runtime;
    private readonly Dictionary<string, object?> _state = new(StringComparer.OrdinalIgnoreCase);

    private StrategyState _stateValue = StrategyState.Created;

    public PythonStrategyAdapter(
        PythonStrategyManifest manifest,
        StrategyContext context,
        IPythonStrategyRuntime runtime)
    {
        _manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    }

    public string Id => _manifest.StrategyId;

    public string Name => _manifest.Name;

    public StrategyState State => _stateValue;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        _stateValue = StrategyState.Running;
        return Task.CompletedTask;
    }

    public Task PauseAsync(CancellationToken cancellationToken = default)
    {
        _stateValue = StrategyState.Paused;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        _stateValue = StrategyState.Stopped;
        return Task.CompletedTask;
    }

    public Task<IEnumerable<string>> SelectMarketsAsync(CancellationToken cancellationToken = default)
    {
        if (_manifest.Parameters.TryGetValue("markets", out var markets) && markets is IEnumerable<object> objects)
        {
            var marketIds = objects
                .Select(x => x?.ToString())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x!)
                .ToArray();

            return Task.FromResult<IEnumerable<string>>(marketIds);
        }

        return Task.FromResult<IEnumerable<string>>(Array.Empty<string>());
    }

    public Task<StrategySignal?> EvaluateEntryAsync(MarketSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        return EvaluateAsync("entry", StrategySignalType.Entry, snapshot, cancellationToken);
    }

    public Task<StrategySignal?> EvaluateExitAsync(MarketSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        return EvaluateAsync("exit", StrategySignalType.Exit, snapshot, cancellationToken);
    }

    public Task OnOrderUpdateAsync(StrategyOrderUpdate update, CancellationToken cancellationToken = default)
    {
        _state["lastOrderUpdate"] = new
        {
            update.ClientOrderId,
            update.MarketId,
            update.Status,
            update.FilledQuantity,
            update.OriginalQuantity,
            update.TimestampUtc
        };
        return Task.CompletedTask;
    }

    private async Task<StrategySignal?> EvaluateAsync(
        string phase,
        StrategySignalType signalType,
        MarketSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var request = new PythonStrategyRequest(
            _manifest.StrategyId,
            phase,
            snapshot.MarketId ?? string.Empty,
            ToSanitizedSnapshot(snapshot),
            _manifest.Parameters,
            DateTimeOffset.UtcNow,
            new Dictionary<string, object?>(_state, StringComparer.OrdinalIgnoreCase));

        var response = await _runtime.EvaluateAsync(_manifest, request, cancellationToken).ConfigureAwait(false);
        foreach (var (key, value) in response.StatePatch)
        {
            _state[key] = value;
        }

        if (string.Equals(response.Action, "skip", StringComparison.OrdinalIgnoreCase))
        {
            await _context.ObservationLogger.LogAsync(new StrategyObservation(
                Id,
                snapshot.MarketId,
                phase,
                "Skipped",
                response.ReasonCode,
                JsonSerializer.Serialize(response.Telemetry, JsonOptions),
                JsonSerializer.Serialize(response.StatePatch, JsonOptions),
                null,
                _manifest.Version,
                null,
                DateTimeOffset.UtcNow), cancellationToken).ConfigureAwait(false);
            return null;
        }

        if (signalType == StrategySignalType.Entry
            && !string.Equals(response.Action, "enter", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (signalType == StrategySignalType.Exit
            && !string.Equals(response.Action, "exit", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var intents = response.Intents.Select(ToIntent).ToList();
        if (intents.Count == 0)
        {
            return null;
        }

        return new StrategySignal(
            signalType,
            snapshot.MarketId ?? intents[0].MarketId,
            response.Reason,
            intents,
            JsonSerializer.Serialize(new
            {
                response.ReasonCode,
                response.Telemetry,
                response.StatePatch,
                generatedStrategyVersion = _manifest.Version
            }, JsonOptions));
    }

    private static StrategyOrderIntent ToIntent(PythonOrderIntent intent)
    {
        return new StrategyOrderIntent(
            intent.MarketId,
            intent.TokenId,
            ParseEnum<OutcomeSide>(intent.Outcome),
            ParseEnum<OrderSide>(intent.Side),
            ParseEnum<OrderType>(intent.OrderType),
            ParseEnum<TimeInForce>(intent.TimeInForce),
            intent.Price,
            intent.Quantity,
            intent.NegRisk,
            ParseEnum<OrderLeg>(intent.Leg));
    }

    private static TEnum ParseEnum<TEnum>(string value)
        where TEnum : struct
    {
        return Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed)
            ? parsed
            : throw new InvalidOperationException($"Invalid Python strategy enum value {typeof(TEnum).Name}:{value}");
    }

    private static object ToSanitizedSnapshot(MarketSnapshot snapshot)
    {
        return new
        {
            market = new
            {
                snapshot.Market?.MarketId,
                snapshot.Market?.Name,
                snapshot.Market?.Category,
                snapshot.Market?.Status,
                snapshot.Market?.ExpiresAtUtc,
                snapshot.Market?.Liquidity,
                snapshot.Market?.Volume24h,
                snapshot.Market?.TokenIds
            },
            yes = ToTopBook(snapshot.YesTopOfBook),
            no = ToTopBook(snapshot.NoTopOfBook),
            snapshot.TimestampUtc
        };
    }

    private static object? ToTopBook(TopOfBookDto? topBook)
    {
        return topBook is null
            ? null
            : new
            {
                topBook.AssetId,
                bestBidPrice = topBook.BestBidPrice?.ToString(),
                bestBidSize = topBook.BestBidSize?.ToString(),
                bestAskPrice = topBook.BestAskPrice?.ToString(),
                bestAskSize = topBook.BestAskSize?.ToString(),
                topBook.Spread,
                topBook.LastUpdatedUtc
            };
    }
}

public sealed class PythonStrategyAdapterFactory : IPythonStrategyAdapterFactory
{
    private readonly IPythonStrategyRuntime _runtime;

    public PythonStrategyAdapterFactory(IPythonStrategyRuntime runtime)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    }

    public ITradingStrategy Create(PythonStrategyManifest manifest, StrategyContext context)
    {
        return new PythonStrategyAdapter(manifest, context, _runtime);
    }
}
