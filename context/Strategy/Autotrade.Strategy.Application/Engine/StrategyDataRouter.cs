// ============================================================================
// 策略数据路由器
// ============================================================================
// 将市场数据路由到各策略的独立 Channel，提供：
// - 隔离性：每个策略有独立的 Channel
// - 背压：慢策略不会阻塞其他策略
// ============================================================================

using Autotrade.Strategy.Application.Contract.Strategies;
using Autotrade.MarketData.Application.Contract.OrderBook;
using Microsoft.Extensions.Logging;

namespace Autotrade.Strategy.Application.Engine;

/// <summary>
/// 策略数据路由器。
/// 将市场快照分发到各策略的独立 Channel。
/// </summary>
public sealed class StrategyDataRouter : IDisposable
{
    private readonly IMarketSnapshotProvider _snapshotProvider;
    private readonly ILogger<StrategyDataRouter> _logger;
    private readonly IOrderBookSubscriptionService? _orderBookSubscriptionService;
    private readonly object _lock = new();
    private readonly Dictionary<string, StrategyMarketChannel> _channels = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HashSet<string>> _subscriptions = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    public StrategyDataRouter(
        IMarketSnapshotProvider snapshotProvider,
        ILogger<StrategyDataRouter> logger,
        IOrderBookSubscriptionService? orderBookSubscriptionService = null)
    {
        _snapshotProvider = snapshotProvider ?? throw new ArgumentNullException(nameof(snapshotProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _orderBookSubscriptionService = orderBookSubscriptionService;
    }

    /// <summary>
    /// Registers a strategy with its dedicated channel.
    /// </summary>
    public StrategyMarketChannel RegisterStrategy(string strategyId, int channelCapacity = 100)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_lock)
        {
            if (_channels.TryGetValue(strategyId, out var existing))
            {
                return existing;
            }

            var channel = new StrategyMarketChannel(channelCapacity);
            _channels[strategyId] = channel;
            _subscriptions[strategyId] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            _logger.LogDebug("Registered strategy channel: {StrategyId}, capacity={Capacity}", strategyId, channelCapacity);

            return channel;
        }
    }

    /// <summary>
    /// Unregisters a strategy and disposes its channel.
    /// </summary>
    public void UnregisterStrategy(string strategyId)
    {
        lock (_lock)
        {
            if (_channels.TryGetValue(strategyId, out var channel))
            {
                channel.Dispose();
                _channels.Remove(strategyId);
                _subscriptions.Remove(strategyId);

                _logger.LogDebug("Unregistered strategy channel: {StrategyId}", strategyId);
            }
        }
    }

    /// <summary>
    /// Updates the market subscriptions for a strategy.
    /// </summary>
    public void UpdateSubscriptions(string strategyId, IEnumerable<string> marketIds)
    {
        lock (_lock)
        {
            if (!_subscriptions.TryGetValue(strategyId, out var subscription))
            {
                return;
            }

            subscription.Clear();
            foreach (var marketId in marketIds)
            {
                if (!string.IsNullOrWhiteSpace(marketId))
                {
                    subscription.Add(marketId);
                }
            }
        }
    }

    /// <summary>
    /// Gets the market subscriptions for a strategy.
    /// </summary>
    public IReadOnlyList<string> GetSubscriptions(string strategyId)
    {
        lock (_lock)
        {
            return _subscriptions.TryGetValue(strategyId, out var subscription)
                ? subscription.ToList()
                : Array.Empty<string>();
        }
    }

    /// <summary>
    /// Gets channel backlog for a strategy.
    /// </summary>
    public int GetChannelBacklog(string strategyId)
    {
        lock (_lock)
        {
            return _channels.TryGetValue(strategyId, out var channel) ? channel.Backlog : 0;
        }
    }

    /// <summary>
    /// Runs the data router, fetching snapshots and distributing to subscribed strategies.
    /// </summary>
    public async Task RunAsync(TimeSpan pollInterval, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await DistributeSnapshotsAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error distributing snapshots");
            }

            await Task.Delay(pollInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task DistributeSnapshotsAsync(CancellationToken cancellationToken)
    {
        // Collect all unique market IDs across strategies
        HashSet<string> allMarketIds;
        Dictionary<string, StrategyMarketChannel> channelsCopy;
        Dictionary<string, HashSet<string>> subscriptionsCopy;

        lock (_lock)
        {
            allMarketIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var subscription in _subscriptions.Values)
            {
                allMarketIds.UnionWith(subscription);
            }

            channelsCopy = new Dictionary<string, StrategyMarketChannel>(_channels, StringComparer.OrdinalIgnoreCase);
            subscriptionsCopy = _subscriptions
                .ToDictionary(
                    kvp => kvp.Key,
                    kvp => new HashSet<string>(kvp.Value, StringComparer.OrdinalIgnoreCase),
                    StringComparer.OrdinalIgnoreCase);
        }

        // Ensure the required order books are subscribed before snapshot reads.
        if (_orderBookSubscriptionService is not null)
        {
            try
            {
                await _orderBookSubscriptionService
                    .EnsureSubscribedMarketsAsync(allMarketIds, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to ensure order book subscriptions");
            }
        }

        if (allMarketIds.Count == 0)
        {
            return;
        }

        // Fetch all snapshots once
        var snapshots = await _snapshotProvider
            .GetSnapshotsAsync(allMarketIds.ToList(), cancellationToken)
            .ConfigureAwait(false);

        var snapshotByMarket = snapshots
            .Where(s => s?.Market is not null && !string.IsNullOrWhiteSpace(s.Market.MarketId))
            .ToDictionary(s => s.Market.MarketId!, StringComparer.OrdinalIgnoreCase);

        // Distribute to each strategy's channel based on subscriptions
        foreach (var (strategyId, channel) in channelsCopy)
        {
            if (!subscriptionsCopy.TryGetValue(strategyId, out var marketIds))
            {
                continue;
            }

            foreach (var marketId in marketIds)
            {
                if (snapshotByMarket.TryGetValue(marketId, out var snapshot))
                {
                    if (!channel.TryWrite(snapshot))
                    {
                        _logger.LogWarning(
                            "Channel full for strategy {StrategyId}, dropping snapshot for {MarketId}",
                            strategyId,
                            marketId);
                    }
                }
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        lock (_lock)
        {
            foreach (var channel in _channels.Values)
            {
                channel.Dispose();
            }

            _channels.Clear();
            _subscriptions.Clear();
        }
    }
}
