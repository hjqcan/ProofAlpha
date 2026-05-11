using System.Threading.Channels;
using Autotrade.Strategy.Application.Contract.Strategies;
using Microsoft.Extensions.Logging;

namespace Autotrade.Strategy.Application.Orders;

/// <summary>
/// Routes order updates to per-strategy channels. Provides isolation and backpressure:
/// each strategy has its own bounded channel, and slow strategies don't block the router.
/// </summary>
public sealed class StrategyOrderUpdateRouter : IDisposable
{
    private readonly ILogger<StrategyOrderUpdateRouter> _logger;
    private readonly object _lock = new();
    private readonly Dictionary<string, StrategyOrderUpdateChannel> _channels = new(StringComparer.OrdinalIgnoreCase);
    private readonly TimeSpan _writeTimeout = TimeSpan.FromSeconds(1);
    private bool _disposed;

    public StrategyOrderUpdateRouter(ILogger<StrategyOrderUpdateRouter> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Registers a strategy with its dedicated channel.
    /// </summary>
    public StrategyOrderUpdateChannel RegisterStrategy(string strategyId, int channelCapacity = 100)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_lock)
        {
            if (_channels.TryGetValue(strategyId, out var existing))
            {
                return existing;
            }

            var channel = new StrategyOrderUpdateChannel(channelCapacity);
            _channels[strategyId] = channel;

            _logger.LogDebug("Registered order update channel: {StrategyId}, capacity={Capacity}", strategyId, channelCapacity);

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

                _logger.LogDebug("Unregistered order update channel: {StrategyId}", strategyId);
            }
        }
    }

    /// <summary>
    /// Gets channel for a strategy.
    /// </summary>
    public StrategyOrderUpdateChannel? GetChannel(string strategyId)
    {
        lock (_lock)
        {
            return _channels.TryGetValue(strategyId, out var channel) ? channel : null;
        }
    }

    /// <summary>
    /// Routes an order update to the appropriate strategy's channel.
    /// Non-blocking: if channel is full after timeout, update is logged and dropped.
    /// </summary>
    public async ValueTask RouteUpdateAsync(StrategyOrderUpdate update, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        StrategyOrderUpdateChannel? channel;
        lock (_lock)
        {
            _channels.TryGetValue(update.StrategyId, out channel);
        }

        if (channel is null)
        {
            _logger.LogWarning("No channel registered for strategy {StrategyId}, dropping order update {ClientOrderId}",
                update.StrategyId, update.ClientOrderId);
            return;
        }

        try
        {
            if (!channel.TryWrite(update))
            {
                // Channel full, try with timeout
                var written = await channel.WriteAsync(update, _writeTimeout, cancellationToken).ConfigureAwait(false);
                if (!written)
                {
                    _logger.LogWarning("Order update channel full for strategy {StrategyId}, dropping update {ClientOrderId}",
                        update.StrategyId, update.ClientOrderId);
                }
            }
        }
        catch (ChannelClosedException)
        {
            _logger.LogDebug("Order update channel closed for strategy {StrategyId}, dropping update {ClientOrderId}",
                update.StrategyId, update.ClientOrderId);
        }
        catch (ObjectDisposedException)
        {
            _logger.LogDebug("Order update channel disposed for strategy {StrategyId}, dropping update {ClientOrderId}",
                update.StrategyId, update.ClientOrderId);
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
        }
    }
}
