// ============================================================================
// 策略订单更新 Channel
// ============================================================================
// 每个策略的独立有界 Channel，用于接收订单状态更新。
// 提供背压和隔离：慢策略不会阻塞轮询器或其他策略。
// ============================================================================

using System.Threading.Channels;
using Autotrade.Strategy.Application.Contract.Strategies;

namespace Autotrade.Strategy.Application.Orders;

/// <summary>
/// 策略订单更新 Channel。
/// 每个策略独立的有界 Channel，用于接收订单状态更新。
/// </summary>
public sealed class StrategyOrderUpdateChannel : IDisposable
{
    private readonly Channel<StrategyOrderUpdate> _channel;
    private readonly int _capacity;
    private volatile bool _disposed;

    public StrategyOrderUpdateChannel(int capacity = 100)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Capacity must be positive.");
        }

        _capacity = capacity;

        // Bounded channel with Wait policy for backpressure:
        // if strategy can't keep up, we wait (but with timeout)
        _channel = Channel.CreateBounded<StrategyOrderUpdate>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = true
        });
    }

    /// <summary>
    /// Current number of items waiting in the channel.
    /// </summary>
    public int Backlog => _channel.Reader.Count;

    /// <summary>
    /// Capacity of the channel.
    /// </summary>
    public int Capacity => _capacity;

    /// <summary>
    /// Writes an update to the channel. Non-blocking if space available.
    /// </summary>
    public bool TryWrite(StrategyOrderUpdate update)
    {
        if (_disposed)
        {
            return false;
        }
        return _channel.Writer.TryWrite(update);
    }

    /// <summary>
    /// Writes an update to the channel with timeout.
    /// </summary>
    public async ValueTask<bool> WriteAsync(StrategyOrderUpdate update, TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (_disposed)
        {
            return false;
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);

        try
        {
            await _channel.Writer.WriteAsync(update, cts.Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Timeout, not cancellation
            return false;
        }
        catch (ChannelClosedException)
        {
            return false;
        }
    }

    /// <summary>
    /// Reads all available updates from the channel.
    /// </summary>
    public IReadOnlyList<StrategyOrderUpdate> TryReadAll()
    {
        if (_disposed)
        {
            return Array.Empty<StrategyOrderUpdate>();
        }

        var updates = new List<StrategyOrderUpdate>();
        while (_channel.Reader.TryRead(out var update))
        {
            updates.Add(update);
        }

        return updates;
    }

    /// <summary>
    /// Waits for updates and reads all available.
    /// </summary>
    public async ValueTask<IReadOnlyList<StrategyOrderUpdate>> ReadBatchAsync(
        int maxCount,
        CancellationToken cancellationToken)
    {
        if (_disposed)
        {
            return Array.Empty<StrategyOrderUpdate>();
        }

        var batch = new List<StrategyOrderUpdate>(Math.Min(maxCount, _capacity));

        // Wait for at least one item
        if (await _channel.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
        {
            while (batch.Count < maxCount && _channel.Reader.TryRead(out var update))
            {
                batch.Add(update);
            }
        }

        return batch;
    }

    /// <summary>
    /// Completes the channel, signaling no more writes.
    /// </summary>
    public void Complete()
    {
        _channel.Writer.TryComplete();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _channel.Writer.TryComplete();
    }
}
