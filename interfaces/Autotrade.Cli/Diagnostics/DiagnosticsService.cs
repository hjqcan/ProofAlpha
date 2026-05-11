// ============================================================================
// Diagnostics Service
// ============================================================================
// 周期性诊断汇总服务，检测系统健康状况并触发告警。
// ============================================================================

using Autotrade.MarketData.Application.WebSocket.Clob;
using Autotrade.MarketData.Application.WebSocket.Rtds;
using Autotrade.Polymarket.Abstractions;
using Autotrade.Strategy.Application.Contract.Strategies;
using Autotrade.Strategy.Application.Engine;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;

namespace Autotrade.Cli.Diagnostics;

/// <summary>
/// 诊断告警级别。
/// </summary>
public enum DiagnosticAlertLevel
{
    Info,
    Warning,
    Critical
}

/// <summary>
/// 诊断告警事件。
/// </summary>
/// <param name="Level">告警级别。</param>
/// <param name="Category">告警类别（api/ws/strategy/error_rate）。</param>
/// <param name="Message">告警消息。</param>
/// <param name="MetricValue">相关指标值。</param>
/// <param name="Threshold">触发阈值。</param>
/// <param name="Timestamp">告警时间。</param>
public sealed record DiagnosticAlert(
    DiagnosticAlertLevel Level,
    string Category,
    string Message,
    double MetricValue,
    double Threshold,
    DateTimeOffset Timestamp);

/// <summary>
/// 诊断服务。
/// 周期性检查系统健康状况，检测异常并触发告警。
/// </summary>
public sealed class DiagnosticsService : BackgroundService
{
    private readonly ILogger<DiagnosticsService> _logger;
    private readonly DiagnosticsOptions _options;
    private readonly IPolymarketClobClient? _clobClient;
    private readonly IClobMarketClient? _clobMarketClient;
    private readonly IRtdsClient? _rtdsClient;
    private readonly IStrategyManager? _strategyManager;

    // Rolling error-rate window (API probe)
    private readonly Queue<bool> _apiProbeWindow = new();
    private int _apiProbeFailures;
    private const int ApiProbeWindowSize = 20; // ~10min if interval=30s

    // WebSocket disconnected tracking (to apply warning/critical thresholds)
    private DateTimeOffset? _clobDisconnectedSinceUtc;
    private DateTimeOffset? _rtdsDisconnectedSinceUtc;

    /// <summary>
    /// 告警事件。
    /// </summary>
    public event EventHandler<DiagnosticAlert>? AlertRaised;

    public DiagnosticsService(
        ILogger<DiagnosticsService> logger,
        IOptions<DiagnosticsOptions> options,
        IPolymarketClobClient? clobClient = null,
        IClobMarketClient? clobMarketClient = null,
        IRtdsClient? rtdsClient = null,
        IStrategyManager? strategyManager = null)
    {
        _logger = logger;
        _options = options.Value;
        _clobClient = clobClient;
        _clobMarketClient = clobMarketClient;
        _rtdsClient = rtdsClient;
        _strategyManager = strategyManager;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("DiagnosticsService 已禁用");
            return;
        }

        _logger.LogInformation(
            "DiagnosticsService 已启动，检查间隔: {Interval}s",
            _options.CheckIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunDiagnosticsAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "诊断检查执行失败");
            }

            await Task.Delay(
                TimeSpan.FromSeconds(_options.CheckIntervalSeconds),
                stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task RunDiagnosticsAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;

        // 1. 检查 API 延迟
        await CheckApiLatencyAsync(now, cancellationToken).ConfigureAwait(false);

        // 2. 检查 WebSocket 连接
        CheckWebSocketHealth(now);

        // 3. 检查策略心跳
        await CheckStrategyHeartbeatsAsync(now, cancellationToken).ConfigureAwait(false);
    }

    private async Task CheckApiLatencyAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (_clobClient is null) return;

        var sw = Stopwatch.StartNew();
        try
        {
            await _clobClient.GetServerTimeAsync(cancellationToken).ConfigureAwait(false);
            sw.Stop();

            var latencyMs = sw.ElapsedMilliseconds;
            RecordApiProbeResult(success: true);

            if (latencyMs >= _options.ApiLatencyCriticalMs)
            {
                RaiseAlert(DiagnosticAlertLevel.Critical, "api",
                    $"API 延迟过高: {latencyMs}ms",
                    latencyMs, _options.ApiLatencyCriticalMs, now);
            }
            else if (latencyMs >= _options.ApiLatencyWarningMs)
            {
                RaiseAlert(DiagnosticAlertLevel.Warning, "api",
                    $"API 延迟偏高: {latencyMs}ms",
                    latencyMs, _options.ApiLatencyWarningMs, now);
            }

            CheckApiErrorRate(now);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "API 延迟检查失败");
            RecordApiProbeResult(success: false);
            CheckApiErrorRate(now);
            RaiseAlert(DiagnosticAlertLevel.Critical, "api",
                $"API 不可达: {ex.Message}",
                -1, 0, now);
        }
    }

    private void CheckWebSocketHealth(DateTimeOffset now)
    {
        // 检查 CLOB WebSocket
        if (_clobMarketClient is not null)
        {
            // 只有在确实存在订阅需求时才把“未连接”视为告警；否则会在启动后产生大量误报。
            // 订阅为空时允许不建立连接（按需连接）。
            if (_clobMarketClient.SubscribedAssets.Count == 0)
            {
                _clobDisconnectedSinceUtc = null;
            }
            else if (_clobMarketClient.IsConnected)
            {
                _clobDisconnectedSinceUtc = null;
            }
            else
            {
                _clobDisconnectedSinceUtc ??= now;
                var downSeconds = (now - _clobDisconnectedSinceUtc.Value).TotalSeconds;

                if (downSeconds >= _options.WsHeartbeatCriticalSeconds)
                {
                    RaiseAlert(DiagnosticAlertLevel.Critical, "ws",
                        $"CLOB WebSocket 未连接: {(int)downSeconds}s",
                        downSeconds, _options.WsHeartbeatCriticalSeconds, now);
                }
                else if (downSeconds >= _options.WsHeartbeatWarningSeconds)
                {
                    RaiseAlert(DiagnosticAlertLevel.Warning, "ws",
                        $"CLOB WebSocket 断线: {(int)downSeconds}s",
                        downSeconds, _options.WsHeartbeatWarningSeconds, now);
                }
            }
        }

        // 检查 RTDS WebSocket
        if (_rtdsClient is not null)
        {
            if (_rtdsClient.IsConnected)
            {
                _rtdsDisconnectedSinceUtc = null;
            }
            else
            {
                _rtdsDisconnectedSinceUtc ??= now;
                var downSeconds = (now - _rtdsDisconnectedSinceUtc.Value).TotalSeconds;

                if (downSeconds >= _options.WsHeartbeatCriticalSeconds)
                {
                    RaiseAlert(DiagnosticAlertLevel.Critical, "ws",
                        $"RTDS WebSocket 未连接: {(int)downSeconds}s",
                        downSeconds, _options.WsHeartbeatCriticalSeconds, now);
                }
                else if (downSeconds >= _options.WsHeartbeatWarningSeconds)
                {
                    RaiseAlert(DiagnosticAlertLevel.Warning, "ws",
                        $"RTDS WebSocket 断线: {(int)downSeconds}s",
                        downSeconds, _options.WsHeartbeatWarningSeconds, now);
                }
            }
        }
    }

    private void RecordApiProbeResult(bool success)
    {
        // Maintain fixed-size rolling window.
        if (_apiProbeWindow.Count >= ApiProbeWindowSize)
        {
            var removed = _apiProbeWindow.Dequeue();
            if (!removed)
            {
                _apiProbeFailures--;
            }
        }

        _apiProbeWindow.Enqueue(success);
        if (!success)
        {
            _apiProbeFailures++;
        }
    }

    private void CheckApiErrorRate(DateTimeOffset now)
    {
        if (_apiProbeWindow.Count == 0)
        {
            return;
        }

        var errorRatePercent = (double)_apiProbeFailures / _apiProbeWindow.Count * 100.0;

        if (errorRatePercent >= _options.ErrorRateCriticalPercent)
        {
            RaiseAlert(DiagnosticAlertLevel.Critical, "error_rate",
                $"API 错误率过高: {errorRatePercent:F1}% (窗口={_apiProbeWindow.Count})",
                errorRatePercent, _options.ErrorRateCriticalPercent, now);
        }
        else if (errorRatePercent >= _options.ErrorRateWarningPercent)
        {
            RaiseAlert(DiagnosticAlertLevel.Warning, "error_rate",
                $"API 错误率偏高: {errorRatePercent:F1}% (窗口={_apiProbeWindow.Count})",
                errorRatePercent, _options.ErrorRateWarningPercent, now);
        }
    }

    private async Task CheckStrategyHeartbeatsAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (_strategyManager is null) return;

        var statuses = await _strategyManager.GetStatusesAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var status in statuses)
        {
            // 跳过非运行状态的策略
            if (status.State != StrategyState.Running) continue;
            if (status.LastHeartbeatUtc is null) continue;

            // 跳过被 Kill Switch 阻塞的策略（策略暂停期间心跳不更新是预期行为）
            if (status.IsKillSwitchBlocked)
            {
                _logger.LogDebug(
                    "Skip heartbeat check for blocked strategy {StrategyId}",
                    status.StrategyId);
                continue;
            }

            var heartbeatAge = (now - status.LastHeartbeatUtc.Value).TotalSeconds;

            if (heartbeatAge >= _options.StrategyLagCriticalSeconds)
            {
                RaiseAlert(DiagnosticAlertLevel.Critical, "strategy",
                    $"策略 {status.StrategyId} 心跳超时: {(int)heartbeatAge}s",
                    heartbeatAge, _options.StrategyLagCriticalSeconds, now);
            }
            else if (heartbeatAge >= _options.StrategyLagWarningSeconds)
            {
                RaiseAlert(DiagnosticAlertLevel.Warning, "strategy",
                    $"策略 {status.StrategyId} 心跳延迟: {(int)heartbeatAge}s",
                    heartbeatAge, _options.StrategyLagWarningSeconds, now);
            }
        }
    }

    private void RaiseAlert(
        DiagnosticAlertLevel level,
        string category,
        string message,
        double metricValue,
        double threshold,
        DateTimeOffset timestamp)
    {
        var alert = new DiagnosticAlert(level, category, message, metricValue, threshold, timestamp);

        // 记录日志
        switch (level)
        {
            case DiagnosticAlertLevel.Critical:
                _logger.LogError("[DIAGNOSTIC CRITICAL] {Category}: {Message}", category, message);
                break;
            case DiagnosticAlertLevel.Warning:
                _logger.LogWarning("[DIAGNOSTIC WARNING] {Category}: {Message}", category, message);
                break;
            default:
                _logger.LogInformation("[DIAGNOSTIC INFO] {Category}: {Message}", category, message);
                break;
        }

        // 触发事件
        AlertRaised?.Invoke(this, alert);
    }
}
