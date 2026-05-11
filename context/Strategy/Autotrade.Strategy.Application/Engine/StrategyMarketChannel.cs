// ============================================================================
// 策略市场数据 Channel
// ============================================================================
// 每个策略的独立有界 Channel，用于接收市场快照。
// 提供背压和隔离：慢策略不会阻塞其他策略或数据生产者。
// ============================================================================

using Autotrade.Strategy.Application.Contract.Strategies;

namespace Autotrade.Strategy.Application.Engine;

/// <summary>
/// 策略市场数据 Channel。
/// 每个策略独立的有界 Channel，用于接收市场快照。
/// </summary>
public sealed class StrategyMarketChannel : IDisposable
{
    private readonly object _gate = new();
    private readonly Queue<string> _pendingMarketIds = new();
    private readonly Dictionary<string, MarketSnapshot> _latestSnapshots = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _available = new(0);
    private readonly int _capacity;
    private volatile bool _disposed;

    public StrategyMarketChannel(int capacity = 100)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Capacity must be positive.");
        }

        _capacity = capacity;

        // Coalescing queue: keep only the newest snapshot per market.
        // If a strategy falls behind, work stays bounded by distinct markets,
        // not by every stale tick that arrived while it was busy.
    }

    /// <summary>
    /// Current number of items waiting in the channel.
    /// </summary>
    public int Backlog
    {
        get
        {
            lock (_gate)
            {
                return _latestSnapshots.Count;
            }
        }
    }

    /// <summary>
    /// Capacity of the channel.
    /// </summary>
    public int Capacity => _capacity;

    /// <summary>
    /// Writes a snapshot to the channel. Non-blocking; if full, oldest item is dropped.
    /// </summary>
    public bool TryWrite(MarketSnapshot snapshot)
    {
        if (_disposed)
        {
            return false;
        }

        var marketId = snapshot.MarketId;
        if (string.IsNullOrWhiteSpace(marketId))
        {
            return false;
        }

        var shouldRelease = false;
        lock (_gate)
        {
            if (_disposed)
            {
                return false;
            }

            if (_latestSnapshots.ContainsKey(marketId))
            {
                _latestSnapshots[marketId] = snapshot;
                return true;
            }

            if (_latestSnapshots.Count >= _capacity)
            {
                DropOldestPendingSnapshot();
            }
            else
            {
                shouldRelease = true;
            }

            _latestSnapshots[marketId] = snapshot;
            _pendingMarketIds.Enqueue(marketId);
        }

        if (shouldRelease)
        {
            _available.Release();
        }

        return true;
    }

    /// <summary>
    /// Writes multiple snapshots to the channel.
    /// </summary>
    public int WriteAll(IEnumerable<MarketSnapshot> snapshots)
    {
        if (_disposed)
        {
            return 0;
        }

        var count = 0;
        foreach (var snapshot in snapshots)
        {
            if (_disposed)
            {
                break;
            }
            if (TryWrite(snapshot))
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// Reads all available snapshots from the channel up to the specified limit.
    /// </summary>
    public async ValueTask<IReadOnlyList<MarketSnapshot>> ReadBatchAsync(
        int maxCount,
        CancellationToken cancellationToken)
    {
        if (_disposed)
        {
            return Array.Empty<MarketSnapshot>();
        }

        if (maxCount <= 0)
        {
            return Array.Empty<MarketSnapshot>();
        }

        await _available.WaitAsync(cancellationToken).ConfigureAwait(false);

        var batch = new List<MarketSnapshot>(Math.Min(maxCount, _capacity));
        lock (_gate)
        {
            if (_disposed)
            {
                return Array.Empty<MarketSnapshot>();
            }

            if (TryDequeueLatestSnapshot(out var first))
            {
                batch.Add(first);
            }

            while (batch.Count < maxCount && _available.Wait(0) && TryDequeueLatestSnapshot(out var snapshot))
            {
                batch.Add(snapshot);
            }
        }

        return batch;
    }

    /// <summary>
    /// Reads available snapshots without blocking.
    /// </summary>
    public IReadOnlyList<MarketSnapshot> TryReadBatch(int maxCount)
    {
        if (_disposed)
        {
            return Array.Empty<MarketSnapshot>();
        }

        if (maxCount <= 0)
        {
            return Array.Empty<MarketSnapshot>();
        }

        var batch = new List<MarketSnapshot>(Math.Min(maxCount, _capacity));
        lock (_gate)
        {
            while (batch.Count < maxCount && _available.Wait(0) && TryDequeueLatestSnapshot(out var snapshot))
            {
                batch.Add(snapshot);
            }
        }

        return batch;
    }

    /// <summary>
    /// Completes the channel, signaling no more writes.
    /// </summary>
    public void Complete()
    {
        Dispose();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        lock (_gate)
        {
            _pendingMarketIds.Clear();
            _latestSnapshots.Clear();
        }

        _available.Release();
    }

    private void DropOldestPendingSnapshot()
    {
        while (_pendingMarketIds.Count > 0)
        {
            var oldestMarketId = _pendingMarketIds.Dequeue();
            if (_latestSnapshots.Remove(oldestMarketId))
            {
                return;
            }
        }
    }

    private bool TryDequeueLatestSnapshot(out MarketSnapshot snapshot)
    {
        while (_pendingMarketIds.Count > 0)
        {
            var marketId = _pendingMarketIds.Dequeue();
            if (_latestSnapshots.Remove(marketId, out snapshot!))
            {
                return true;
            }
        }

        snapshot = null!;
        return false;
    }
}
