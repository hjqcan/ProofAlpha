using Autotrade.Trading.Application.Contract.Accounts;
using Autotrade.Trading.Application.Contract.Execution;
using Autotrade.Trading.Application.Contract.Risk;
using Autotrade.Trading.Application.Execution;
using Autotrade.Trading.Domain.Shared.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Autotrade.Trading.Infra.BackgroundJobs.Workers;

/// <summary>
/// 账户同步后台服务。
/// 定期从 Polymarket 同步余额和持仓。
/// </summary>
public sealed class AccountSyncWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AccountSyncOptions _options;
    private readonly ExecutionOptions _executionOptions;
    private readonly ILogger<AccountSyncWorker> _logger;

    public AccountSyncWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<AccountSyncOptions> options,
        IOptions<ExecutionOptions> executionOptions,
        ILogger<AccountSyncWorker> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _executionOptions = executionOptions?.Value ?? throw new ArgumentNullException(nameof(executionOptions));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("账户同步服务已禁用");
            return;
        }

        // Paper 模式跳过同步
        if (_executionOptions.Mode == ExecutionMode.Paper)
        {
            _logger.LogInformation("Paper 模式，跳过账户同步");
            return;
        }

        _logger.LogInformation(
            "账户同步服务已启动，同步间隔: {Interval} 秒",
            _options.SyncIntervalSeconds);

        // 等待初始延迟，让其他服务先启动
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken).ConfigureAwait(false);

        var syncInterval = TimeSpan.FromSeconds(Math.Max(10, _options.SyncIntervalSeconds));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PerformSyncAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "账户同步过程中发生异常");
            }

            try
            {
                await Task.Delay(syncInterval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("账户同步服务已停止");
    }

    private async Task PerformSyncAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var syncService = scope.ServiceProvider.GetRequiredService<IAccountSyncService>();

        _logger.LogDebug("执行定期账户同步...");

        var result = await syncService.SyncAllAsync(cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            _logger.LogWarning("定期账户同步失败: {Error}", result.ErrorMessage);
            return;
        }

        if (result.HasDrift)
        {
            _logger.LogWarning(
                "检测到账户漂移: PositionDrifts={PositionDrifts}, UnknownOrders={UnknownOrders}",
                result.Positions?.DriftCount ?? 0,
                result.OpenOrders?.UnknownExternalOrderCount ?? 0);

            if (_options.TriggerKillSwitchOnDrift)
            {
                var riskManager = scope.ServiceProvider.GetRequiredService<IRiskManager>();
                var contextJson = System.Text.Json.JsonSerializer.Serialize(new
                {
                    result.Positions?.DriftCount,
                    result.OpenOrders?.UnknownExternalOrderCount,
                    result.OpenOrders?.MissingInternalOrderCount
                });

                await riskManager.ActivateKillSwitchAsync(
                        KillSwitchLevel.HardStop,
                        "RISK_EXTERNAL_ACCOUNT_DRIFT",
                        "External account drift detected (positions/open orders reconciliation)",
                        contextJson,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }
}
