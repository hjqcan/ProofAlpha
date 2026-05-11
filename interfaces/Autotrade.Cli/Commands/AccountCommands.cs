// ============================================================================
// Account 命令处理器
// ============================================================================

using System.Text.Json;
using Autotrade.Trading.Application.Contract.Accounts;
using Autotrade.Trading.Application.Contract.Execution;
using Autotrade.Trading.Application.Contract.Risk;
using Autotrade.Trading.Application.Execution;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Autotrade.Cli.Commands;

/// <summary>
/// 账户管理命令处理器。
/// </summary>
public static class AccountCommands
{
    /// <summary>
    /// 执行 account status 命令。
    /// </summary>
    public static async Task<int> ExecuteStatusAsync(CommandContext context)
    {
        using var scope = context.Host.Services.CreateScope();

        // CLI 命令不启动 HostedServices，因此这里显式执行 bootstrap（fail-fast）
        var bootstrapper = scope.ServiceProvider.GetRequiredService<TradingAccountBootstrapper>();
        await bootstrapper.BootstrapAsync().ConfigureAwait(false);

        var accountContext = scope.ServiceProvider.GetRequiredService<TradingAccountContext>();
        var executionOptions = scope.ServiceProvider.GetRequiredService<IOptions<ExecutionOptions>>().Value;
        var capitalOptions = scope.ServiceProvider.GetRequiredService<IOptions<RiskCapitalOptions>>().Value;
        var capitalProvider = scope.ServiceProvider.GetRequiredService<IRiskCapitalProvider>();
        var snapshotStore = scope.ServiceProvider.GetRequiredService<ExternalAccountSnapshotStore>();

        // 获取风控 snapshot
        var riskSnapshot = capitalProvider.GetSnapshot();

        // 获取外部快照
        var balanceSnapshot = snapshotStore.BalanceSnapshot;
        var positionsSnapshot = snapshotStore.PositionsSnapshot;
        var lastSyncTime = snapshotStore.LastSyncTime;

        var result = new
        {
            Mode = executionOptions.Mode.ToString(),
            AccountKey = accountContext.AccountKey,
            TradingAccountId = accountContext.TradingAccountId,
            RiskLimit = new
            {
                capitalOptions.TotalCapital,
                capitalOptions.AvailableCapital
            },
            EffectiveCapital = new
            {
                riskSnapshot.TotalCapital,
                riskSnapshot.AvailableCapital,
                riskSnapshot.RealizedDailyPnl
            },
            ExternalBalance = balanceSnapshot is null ? null : new
            {
                balanceSnapshot.BalanceUsdc,
                balanceSnapshot.AllowanceUsdc,
                SyncedAt = balanceSnapshot.SyncedAtUtc
            },
            ExternalPositions = positionsSnapshot?.Select(p => new
            {
                p.MarketId,
                p.Outcome,
                p.Quantity,
                p.AvgPrice
            }).ToList(),
            LastSyncTime = lastSyncTime
        };

        if (context.JsonOutput)
        {
            Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        }
        else
        {
            Console.WriteLine("=== Autotrade Account Status ===");
            Console.WriteLine();

            Console.WriteLine($"Mode:             {result.Mode}");
            Console.WriteLine($"Account Key:      {result.AccountKey}");
            Console.WriteLine($"Trading Account:  {result.TradingAccountId}");
            Console.WriteLine();

            Console.WriteLine("--- 风控上限 (Risk:Capital) ---");
            Console.WriteLine($"  Total Capital:     {capitalOptions.TotalCapital:N2} USDC");
            Console.WriteLine($"  Available Capital: {capitalOptions.AvailableCapital:N2} USDC");
            Console.WriteLine();

            Console.WriteLine("--- 有效资金 (Effective) ---");
            Console.WriteLine($"  Total Capital:     {riskSnapshot.TotalCapital:N2} USDC");
            Console.WriteLine($"  Available Capital: {riskSnapshot.AvailableCapital:N2} USDC");
            Console.WriteLine($"  Realized Daily PnL: {riskSnapshot.RealizedDailyPnl:N2} USDC");
            Console.WriteLine();

            if (balanceSnapshot is not null)
            {
                Console.WriteLine("--- 外部余额快照 ---");
                Console.WriteLine($"  Balance:     {balanceSnapshot.BalanceUsdc:N2} USDC");
                Console.WriteLine($"  Allowance:   {balanceSnapshot.AllowanceUsdc:N2} USDC");
                Console.WriteLine($"  Synced At:   {balanceSnapshot.SyncedAtUtc:yyyy-MM-dd HH:mm:ss} UTC");
                Console.WriteLine();
            }

            if (positionsSnapshot is not null && positionsSnapshot.Count > 0)
            {
                Console.WriteLine($"--- 外部持仓快照 ({positionsSnapshot.Count} positions) ---");
                foreach (var p in positionsSnapshot.Take(10))
                {
                    var marketIdShort = p.MarketId.Length > 16 ? p.MarketId[..16] + "..." : p.MarketId;
                    Console.WriteLine($"  {marketIdShort} | {p.Outcome,-4} | Qty: {p.Quantity:N4} | AvgPrice: {p.AvgPrice:N4}");
                }

                if (positionsSnapshot.Count > 10)
                {
                    Console.WriteLine($"  ... 还有 {positionsSnapshot.Count - 10} 个持仓");
                }

                Console.WriteLine();
            }

            if (lastSyncTime.HasValue)
            {
                Console.WriteLine($"Last Sync: {lastSyncTime.Value:yyyy-MM-dd HH:mm:ss} UTC");
            }
            else
            {
                Console.WriteLine("Last Sync: N/A (未同步)");
            }
        }

        return 0;
    }

    /// <summary>
    /// 执行 account sync 命令。
    /// </summary>
    public static async Task<int> ExecuteSyncAsync(CommandContext context, bool failOnDrift)
    {
        using var scope = context.Host.Services.CreateScope();
        var executionOptions = scope.ServiceProvider.GetRequiredService<IOptions<ExecutionOptions>>().Value;

        if (executionOptions.Mode == ExecutionMode.Paper)
        {
            Console.WriteLine("Paper 模式，跳过同步");
            return 0;
        }

        // CLI 命令不启动 HostedServices，因此这里显式执行 bootstrap（fail-fast）
        var bootstrapper = scope.ServiceProvider.GetRequiredService<TradingAccountBootstrapper>();
        await bootstrapper.BootstrapAsync().ConfigureAwait(false);

        var syncService = scope.ServiceProvider.GetRequiredService<IAccountSyncService>();

        Console.WriteLine("执行账户同步...");

        var syncResult = await syncService.SyncAllAsync().ConfigureAwait(false);

        if (!syncResult.IsSuccess)
        {
            Console.WriteLine($"同步失败: {syncResult.ErrorMessage}");
            return 1;
        }

        Console.WriteLine();
        Console.WriteLine("=== 同步结果 ===");

        if (syncResult.Balance is not null)
        {
            Console.WriteLine($"余额: {syncResult.Balance.BalanceUsdc:N2} USDC");
            Console.WriteLine($"Allowance: {syncResult.Balance.AllowanceUsdc:N2} USDC");
        }

        if (syncResult.Positions is not null)
        {
            Console.WriteLine($"持仓数: {syncResult.Positions.PositionCount}");
            Console.WriteLine($"漂移数: {syncResult.Positions.DriftCount}");

            if (syncResult.Positions.Drifts is { Count: > 0 })
            {
                Console.WriteLine("--- 持仓漂移 ---");
                foreach (var drift in syncResult.Positions.Drifts)
                {
                    var marketIdShort = drift.MarketId.Length > 16 ? drift.MarketId[..16] + "..." : drift.MarketId;
                    Console.WriteLine($"  {marketIdShort} | {drift.Outcome} | Type: {drift.DriftType} | Internal: {drift.InternalQuantity:N4} | External: {drift.ExternalQuantity:N4}");
                }
            }
        }

        if (syncResult.OpenOrders is not null)
        {
            Console.WriteLine($"外部挂单数: {syncResult.OpenOrders.OpenOrderCount}");
            Console.WriteLine($"未知外部挂单: {syncResult.OpenOrders.UnknownExternalOrderCount}");
            Console.WriteLine($"缺失内部挂单: {syncResult.OpenOrders.MissingInternalOrderCount}");
        }

        Console.WriteLine();
        Console.WriteLine($"HasDrift: {syncResult.HasDrift}");

        if (failOnDrift && syncResult.HasDrift)
        {
            Console.WriteLine("发现漂移，退出码 = 1");
            return 1;
        }

        return 0;
    }
}
