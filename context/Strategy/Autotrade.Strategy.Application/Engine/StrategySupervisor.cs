// ============================================================================
// 策略监督器
// ============================================================================
// 管理单个策略的运行时状态和生命周期：
// - 启动/暂停/恢复/停止控制
// - 自动重启（达到错误阈值后停止）
// - 运行时统计和状态发布
// ============================================================================

using Autotrade.Strategy.Application.Contract.Strategies;
using Autotrade.Strategy.Application.Observability;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Autotrade.Strategy.Application.Engine;

/// <summary>
/// 策略监督器。
/// 管理单个策略实例的运行时状态。
/// </summary>
public sealed class StrategySupervisor
{
    private readonly StrategyDescriptor _descriptor;
    private readonly StrategyRunner _runner;
    private readonly StrategyControl _control;
    private readonly StrategyEngineOptions _options;
    private readonly ILogger<StrategySupervisor> _logger;
    private readonly Func<StrategyStatus, Task> _onStatusChanged;
    private readonly IServiceScope _scope;
    private readonly ITradingStrategy _strategy;
    private readonly StrategyMarketChannel? _channel;
    private readonly Func<bool>? _isBlocked;

    private CancellationTokenSource? _cts;
    private Task? _runTask;
    private StrategyState _state;
    private int _restartCount;
    private DateTimeOffset? _startedAtUtc;
    private DateTimeOffset? _lastDecisionAtUtc;
    private DateTimeOffset? _lastHeartbeatUtc;
    private string? _lastError;
    private IReadOnlyList<string> _activeMarkets = Array.Empty<string>();
    private long _cycleCount;
    private long _snapshotsProcessed;

    public StrategySupervisor(
        StrategyDescriptor descriptor,
        ITradingStrategy strategy,
        StrategyRunner runner,
        StrategyControl control,
        IOptions<StrategyEngineOptions> options,
        ILogger<StrategySupervisor> logger,
        Func<StrategyStatus, Task> onStatusChanged,
        IServiceScope scope,
        StrategyMarketChannel? channel = null,
        Func<bool>? isBlocked = null)
    {
        _descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
        _strategy = strategy ?? throw new ArgumentNullException(nameof(strategy));
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _control = control ?? throw new ArgumentNullException(nameof(control));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _onStatusChanged = onStatusChanged ?? throw new ArgumentNullException(nameof(onStatusChanged));
        _scope = scope ?? throw new ArgumentNullException(nameof(scope));
        _channel = channel;
        _isBlocked = isBlocked;
    }

    public StrategyState State => _state;

    public int RestartCount => _restartCount;

    public string StrategyId => _descriptor.StrategyId;

    public string StrategyName => _descriptor.Name;

    public ITradingStrategy Strategy => _strategy;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_runTask is not null && !_runTask.IsCompleted)
        {
            return;
        }

        _control.Resume();
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _state = StrategyState.Running;
        _startedAtUtc = DateTimeOffset.UtcNow;
        _lastError = null;

        await PublishStatusAsync().ConfigureAwait(false);

        _runTask = Task.Run(() => RunWithSupervisionAsync(_cts.Token), CancellationToken.None);
    }

    public async Task PauseAsync(CancellationToken cancellationToken)
    {
        if (_state != StrategyState.Running)
        {
            return;
        }

        _control.Pause();
        _state = StrategyState.Paused;
        await PublishStatusAsync().ConfigureAwait(false);
    }

    public async Task ResumeAsync(CancellationToken cancellationToken)
    {
        if (_state != StrategyState.Paused)
        {
            return;
        }

        _control.Resume();
        _state = StrategyState.Running;
        await PublishStatusAsync().ConfigureAwait(false);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_cts is null)
        {
            return;
        }

        _cts.Cancel();
        _control.Resume();

        if (_runTask is not null)
        {
            await Task.WhenAny(_runTask, Task.Delay(TimeSpan.FromSeconds(5), cancellationToken))
                .ConfigureAwait(false);
        }

        _state = StrategyState.Stopped;
        await PublishStatusAsync().ConfigureAwait(false);
    }

    public async Task NotifyDecisionAsync(DateTimeOffset timestampUtc)
    {
        _lastDecisionAtUtc = timestampUtc;
        await PublishStatusAsync().ConfigureAwait(false);
    }

    public async Task NotifyHeartbeatAsync(DateTimeOffset timestampUtc)
    {
        _lastHeartbeatUtc = timestampUtc;
        await PublishStatusAsync().ConfigureAwait(false);
    }

    public async Task NotifyStatsAsync(IReadOnlyList<string> activeMarkets, long cycleCount, long snapshotsProcessed)
    {
        _activeMarkets = activeMarkets;
        _cycleCount = cycleCount;
        _snapshotsProcessed = snapshotsProcessed;
        await PublishStatusAsync().ConfigureAwait(false);
    }

    private async Task RunWithSupervisionAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await _runner.RunAsync(cancellationToken).ConfigureAwait(false);
                    break;
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _lastError = ex.Message;
                    _state = StrategyState.Faulted;
                StrategyMetrics.RecordError(_descriptor.StrategyId, "runner");

                _logger.LogError(ex, "Strategy {StrategyId} faulted", _descriptor.StrategyId);

                    await PublishStatusAsync().ConfigureAwait(false);

                    _restartCount++;
                    if (_restartCount > _options.MaxRestartAttempts)
                    {
                    _logger.LogWarning("Strategy {StrategyId} exceeded restart limit", _descriptor.StrategyId);
                    break;
                }

                StrategyMetrics.RecordRestart(_descriptor.StrategyId);

                    await Task.Delay(TimeSpan.FromSeconds(_options.RestartDelaySeconds), cancellationToken)
                        .ConfigureAwait(false);

                    _state = StrategyState.Running;
                    _lastError = null;
                    await PublishStatusAsync().ConfigureAwait(false);
                }
            }
        }
        finally
        {
            _state = StrategyState.Stopped;
            await PublishStatusAsync().ConfigureAwait(false);
            _scope.Dispose();
        }
    }

    private async Task PublishStatusAsync()
    {
        var channelBacklog = _channel?.Backlog ?? 0;
        var isBlocked = false;
        if (_isBlocked is not null)
        {
            try
            {
                isBlocked = _isBlocked.Invoke();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to evaluate kill switch blocked flag for strategy {StrategyId}",
                    _descriptor.StrategyId);
            }
        }

        var status = new StrategyStatus(
            _descriptor.StrategyId,
            _descriptor.Name,
            _state,
            _descriptor.Enabled,
            _descriptor.ConfigVersion,
            _restartCount,
            _startedAtUtc,
            _lastDecisionAtUtc,
            _lastHeartbeatUtc,
            _lastError,
            _activeMarkets,
            _cycleCount,
            _snapshotsProcessed,
            channelBacklog,
            isBlocked,
            BlockedReason: ResolveBlockedReason(isBlocked));

        await _onStatusChanged(status).ConfigureAwait(false);
    }

    private StrategyBlockedReason? ResolveBlockedReason(bool isKillSwitchBlocked)
    {
        if (_state == StrategyState.Faulted || !string.IsNullOrWhiteSpace(_lastError))
        {
            return StrategyBlockedReasons.StrategyFault(_lastError);
        }

        if (!_descriptor.Enabled)
        {
            return StrategyBlockedReasons.DisabledConfig(_descriptor.StrategyId);
        }

        if (isKillSwitchBlocked)
        {
            return StrategyBlockedReasons.KillSwitch();
        }

        return null;
    }
}
