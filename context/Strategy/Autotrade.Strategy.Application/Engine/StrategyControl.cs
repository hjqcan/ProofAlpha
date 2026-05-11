// ============================================================================
// 策略控制器
// ============================================================================
// 控制策略的暂停/恢复状态。
// ============================================================================

namespace Autotrade.Strategy.Application.Engine;

/// <summary>
/// 策略控制器。
/// 控制策略的暂停/恢复状态。
/// </summary>
public sealed class StrategyControl
{
    private readonly AsyncManualResetEvent _runGate = new(true);

    /// <summary>
    /// 暂停策略。
    /// </summary>
    public void Pause() => _runGate.Reset();

    /// <summary>
    /// 恢复策略。
    /// </summary>
    public void Resume() => _runGate.Set();

    /// <summary>
    /// 等待暂停状态解除。
    /// </summary>
    public Task WaitIfPausedAsync(CancellationToken cancellationToken)
    {
        return _runGate.WaitAsync(cancellationToken);
    }
}
