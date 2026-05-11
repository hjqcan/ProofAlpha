using Autotrade.Polymarket.Options;
using Autotrade.Trading.Application.Contract.Accounts;
using Autotrade.Trading.Application.Contract.Execution;
using Autotrade.Trading.Application.Contract.Risk;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Autotrade.Trading.Application.Execution;

/// <summary>
/// Trading 账户启动引导器（可被 HostedService 与 CLI 复用）。
/// - Provision TradingAccount（使用 Risk:Capital 作为风控上限写入账户表）
/// - Live 模式按配置执行外部快照同步 + 对账（fail-fast）
/// </summary>
public sealed class TradingAccountBootstrapper
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ExecutionOptions _executionOptions;
    private readonly PaperTradingOptions _paperOptions;
    private readonly PolymarketClobOptions _clobOptions;
    private readonly RiskCapitalOptions _capitalOptions;
    private readonly TradingAccountContext _accountContext;
    private readonly ILogger<TradingAccountBootstrapper> _logger;

    public TradingAccountBootstrapper(
        IServiceScopeFactory scopeFactory,
        IOptions<ExecutionOptions> executionOptions,
        IOptions<PaperTradingOptions> paperOptions,
        IOptions<PolymarketClobOptions> clobOptions,
        IOptions<RiskCapitalOptions> capitalOptions,
        TradingAccountContext accountContext,
        ILogger<TradingAccountBootstrapper> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _executionOptions = executionOptions?.Value ?? throw new ArgumentNullException(nameof(executionOptions));
        _paperOptions = paperOptions?.Value ?? throw new ArgumentNullException(nameof(paperOptions));
        _clobOptions = clobOptions?.Value ?? throw new ArgumentNullException(nameof(clobOptions));
        _capitalOptions = capitalOptions?.Value ?? throw new ArgumentNullException(nameof(capitalOptions));
        _accountContext = accountContext ?? throw new ArgumentNullException(nameof(accountContext));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task BootstrapAsync(CancellationToken cancellationToken = default)
    {
        var mode = _executionOptions.Mode;

        var accountKey = mode switch
        {
            ExecutionMode.Live => TradingAccountKeyResolver.ResolveForLiveOrThrow(_clobOptions),
            ExecutionMode.Paper => TradingAccountKeyResolver.ResolveForPaper(_paperOptions),
            _ => throw new InvalidOperationException($"Unknown ExecutionMode: {mode}")
        };

        using var scope = _scopeFactory.CreateScope();
        var provisioner = scope.ServiceProvider.GetRequiredService<ITradingAccountProvisioner>();

        var tradingAccountId = await provisioner
            .ProvisionAsync(
                accountKey,
                _capitalOptions.TotalCapital,
                _capitalOptions.AvailableCapital,
                cancellationToken)
            .ConfigureAwait(false);

        _accountContext.Initialize(tradingAccountId, accountKey);

        _logger.LogInformation(
            "Trading account provisioned: Mode={Mode}, AccountKey={AccountKey}, TradingAccountId={TradingAccountId}",
            mode,
            _accountContext.AccountKey,
            _accountContext.TradingAccountId);

        // Live 模式启动：执行外部快照同步 + 对账（fail-fast）
        if (mode == ExecutionMode.Live)
        {
            await PerformStartupSyncAsync(scope, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task PerformStartupSyncAsync(IServiceScope scope, CancellationToken cancellationToken)
    {
        var syncOptions = scope.ServiceProvider.GetRequiredService<IOptions<AccountSyncOptions>>().Value;
        if (!syncOptions.SyncOnStartup)
        {
            _logger.LogInformation("启动同步已禁用，跳过");
            return;
        }

        var syncService = scope.ServiceProvider.GetRequiredService<IAccountSyncService>();
        var maxRetries = Math.Max(1, syncOptions.StartupMaxRetries);
        var retryDelay = TimeSpan.FromMilliseconds(Math.Max(100, syncOptions.StartupRetryDelayMs));

        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            _logger.LogInformation("执行启动同步，尝试 {Attempt}/{MaxRetries}...", attempt, maxRetries);

            var result = await syncService.SyncAllAsync(cancellationToken).ConfigureAwait(false);

            if (result.IsSuccess)
            {
                if (result.HasDrift)
                {
                    _logger.LogWarning(
                        "启动同步发现漂移: PositionDrifts={PositionDrifts}, UnknownOrders={UnknownOrders}, MissingOrders={MissingOrders}",
                        result.Positions?.DriftCount ?? 0,
                        result.OpenOrders?.UnknownExternalOrderCount ?? 0,
                        result.OpenOrders?.MissingInternalOrderCount ?? 0);

                    if (syncOptions.FailFastOnStartupDrift)
                    {
                        throw new InvalidOperationException(
                            $"启动同步发现账户漂移（fail-fast）: " +
                            $"PositionDrifts={result.Positions?.DriftCount ?? 0}, " +
                            $"UnknownOrders={result.OpenOrders?.UnknownExternalOrderCount ?? 0}, " +
                            $"MissingOrders={result.OpenOrders?.MissingInternalOrderCount ?? 0}");
                    }
                }

                _logger.LogInformation(
                    "启动同步完成: Balance={Balance} USDC, Allowance={Allowance} USDC, Positions={Positions}, OpenOrders={OpenOrders}",
                    result.Balance?.BalanceUsdc,
                    result.Balance?.AllowanceUsdc,
                    result.Positions?.PositionCount ?? 0,
                    result.OpenOrders?.OpenOrderCount ?? 0);

                return;
            }

            _logger.LogWarning("启动同步失败: {Error}", result.ErrorMessage);

            if (attempt < maxRetries)
            {
                await Task.Delay(retryDelay, cancellationToken).ConfigureAwait(false);
            }
        }

        throw new InvalidOperationException(
            $"启动同步失败，已达最大重试次数 {maxRetries}（Live 模式 fail-fast）");
    }
}

