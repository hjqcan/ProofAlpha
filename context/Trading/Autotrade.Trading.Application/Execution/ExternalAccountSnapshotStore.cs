using Autotrade.Trading.Application.Contract.Accounts;
using System.Linq;

namespace Autotrade.Trading.Application.Execution;

/// <summary>
/// 外部账户快照存储（Singleton）。
/// 用于在不同 scope/后台任务之间共享最新一次外部同步结果（余额/持仓等）。
/// </summary>
public sealed class ExternalAccountSnapshotStore
{
    private readonly object _lock = new();
    private ExternalBalanceSnapshot? _balanceSnapshot;
    private IReadOnlyList<ExternalPositionSnapshot>? _positionsSnapshot;
    private DateTimeOffset? _lastSyncTime;

    public DateTimeOffset? LastSyncTime
    {
        get
        {
            lock (_lock)
            {
                return _lastSyncTime;
            }
        }
    }

    public ExternalBalanceSnapshot? BalanceSnapshot
    {
        get
        {
            lock (_lock)
            {
                return _balanceSnapshot;
            }
        }
    }

    public IReadOnlyList<ExternalPositionSnapshot>? PositionsSnapshot
    {
        get
        {
            lock (_lock)
            {
                return _positionsSnapshot;
            }
        }
    }

    public void SetBalanceSnapshot(ExternalBalanceSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        lock (_lock)
        {
            _balanceSnapshot = snapshot;
            _lastSyncTime = snapshot.SyncedAtUtc;
        }
    }

    public void SetPositionsSnapshot(IReadOnlyList<ExternalPositionSnapshot> snapshots, DateTimeOffset syncedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(snapshots);

        // 防御性复制，避免外部修改
        var copy = snapshots.ToList();

        lock (_lock)
        {
            _positionsSnapshot = copy;
            _lastSyncTime = syncedAtUtc;
        }
    }

    public void Touch(DateTimeOffset syncedAtUtc)
    {
        lock (_lock)
        {
            _lastSyncTime = syncedAtUtc;
        }
    }
}

