// ============================================================================
// 异步手动重置事件
// ============================================================================
// 用于策略暂停/恢复的异步等待机制。
// ============================================================================

namespace Autotrade.Strategy.Application.Engine;

/// <summary>
/// 异步手动重置事件。
/// 用于策略暂停/恢复的异步等待机制。
/// </summary>
public sealed class AsyncManualResetEvent
{
    private TaskCompletionSource<bool> _tcs;

    public AsyncManualResetEvent(bool initialState)
    {
        _tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (initialState)
        {
            _tcs.TrySetResult(true);
        }
    }

    /// <summary>
    /// 等待事件被设置。
    /// </summary>
    public Task WaitAsync(CancellationToken cancellationToken)
    {
        return _tcs.Task.WaitAsync(cancellationToken);
    }

    /// <summary>
    /// 设置事件（允许等待者继续）。
    /// </summary>
    public void Set()
    {
        _tcs.TrySetResult(true);
    }

    /// <summary>
    /// 重置事件（阻止后续等待者）。
    /// </summary>
    public void Reset()
    {
        while (true)
        {
            var tcs = _tcs;
            if (!tcs.Task.IsCompleted)
            {
                return;
            }

            var newTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (Interlocked.CompareExchange(ref _tcs, newTcs, tcs) == tcs)
            {
                return;
            }
        }
    }
}
