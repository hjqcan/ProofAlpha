// ============================================================================
// Status 命令处理器
// ============================================================================

using System.Text.Json;
using Autotrade.Strategy.Application.Engine;
using Autotrade.Trading.Application.Contract.Compliance;
using Autotrade.Trading.Application.Contract.Execution;
using Autotrade.Trading.Application.Contract.Repositories;
using Autotrade.Trading.Application.Contract.Risk;
using Autotrade.Trading.Application.Risk;
using Autotrade.Trading.Domain.Shared.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Autotrade.Cli.Commands;

/// <summary>
/// 处理 status 命令：显示系统状态。
/// </summary>
public static class StatusCommand
{
    /// <summary>
    /// 执行 status 命令。
    /// </summary>
    public static async Task<int> ExecuteAsync(CommandContext context)
    {
        using var scope = context.Host.Services.CreateScope();
        var strategyManager = scope.ServiceProvider.GetRequiredService<IStrategyManager>();
        var stateRepository = scope.ServiceProvider.GetRequiredService<IStrategyRunStateRepository>();
        var orderRepository = scope.ServiceProvider.GetRequiredService<IOrderRepository>();
        var positionRepository = scope.ServiceProvider.GetRequiredService<IPositionRepository>();
        var riskEventRepository = scope.ServiceProvider.GetRequiredService<IRiskEventRepository>();
        var executionOptions = scope.ServiceProvider.GetRequiredService<IOptions<ExecutionOptions>>().Value;
        var complianceGuard = scope.ServiceProvider.GetRequiredService<IComplianceGuard>();
        var engineOptions = scope.ServiceProvider.GetRequiredService<IOptionsMonitor<StrategyEngineOptions>>().CurrentValue;
        var killSwitchControl = scope.ServiceProvider.GetRequiredService<IOptionsMonitor<KillSwitchControlOptions>>().CurrentValue;

        // 获取已注册策略描述和运行状态
        var descriptors = strategyManager.GetRegisteredStrategies();
        var states = await stateRepository.GetAllAsync().ConfigureAwait(false);

        // 合并配置与运行时状态
        var merged = descriptors.Select(d =>
        {
            var state = states.FirstOrDefault(s => string.Equals(s.StrategyId, d.StrategyId, StringComparison.OrdinalIgnoreCase));
            var hasDesired = engineOptions.DesiredStates.TryGetValue(d.StrategyId, out var desired);
            return new
            {
                d.StrategyId,
                d.Name,
                d.Enabled,
                d.ConfigVersion,
                d.OptionsSectionName,
                DesiredState = hasDesired ? desired.ToString() : "Unspecified",
                State = state?.State.ToString() ?? "Unknown",
                state?.LastDecisionAtUtc,
                state?.LastHeartbeatUtc,
                state?.LastError,
                // 运行时统计
                ActiveMarkets = state?.ActiveMarkets ?? Array.Empty<string>(),
                CycleCount = state?.CycleCount ?? 0,
                SnapshotsProcessed = state?.SnapshotsProcessed ?? 0,
                ChannelBacklog = state?.ChannelBacklog ?? 0
            };
        }).ToList();

        // Orders / Positions / Risk events (persisted)
        var openPending = await orderRepository.GetPagedAsync(1, 1, null, null, OrderStatus.Pending, null, null).ConfigureAwait(false);
        var openOpen = await orderRepository.GetPagedAsync(1, 1, null, null, OrderStatus.Open, null, null).ConfigureAwait(false);
        var openPartial = await orderRepository.GetPagedAsync(1, 1, null, null, OrderStatus.PartiallyFilled, null, null).ConfigureAwait(false);
        var openOrdersTotal = openPending.TotalCount + openOpen.TotalCount + openPartial.TotalCount;

        var nonZeroPositions = await positionRepository.GetNonZeroAsync().ConfigureAwait(false);
        var recentRiskEvents = await riskEventRepository.QueryAsync(limit: 10).ConfigureAwait(false);
        var compliance = complianceGuard.CheckConfiguration(executionOptions.Mode);

        var result = new
        {
            TimestampUtc = DateTimeOffset.UtcNow,
            ExecutionMode = executionOptions.Mode.ToString(),
            Compliance = new
            {
                compliance.Enabled,
                compliance.IsCompliant,
                compliance.BlocksOrders,
                Issues = compliance.Issues
            },
            KillSwitchDesired = new
            {
                killSwitchControl.GlobalActive,
                GlobalLevel = killSwitchControl.GlobalLevel.ToString(),
                killSwitchControl.GlobalReasonCode,
                killSwitchControl.GlobalReason
            },
            OpenOrders = new
            {
                Total = openOrdersTotal,
                Pending = openPending.TotalCount,
                Open = openOpen.TotalCount,
                PartiallyFilled = openPartial.TotalCount
            },
            Positions = new
            {
                NonZeroCount = nonZeroPositions.Count
            },
            RecentRiskEvents = recentRiskEvents,
            Strategies = merged
        };

        if (context.JsonOutput)
        {
            Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        }
        else
        {
            Console.WriteLine($"Execution: {result.ExecutionMode}");
            Console.WriteLine($"Compliance: {(compliance.IsCompliant ? "OK" : "Issue")} blocksOrders={compliance.BlocksOrders} issues={compliance.Issues.Count}");
            Console.WriteLine($"KillSwitch(desired): {(killSwitchControl.GlobalActive ? "Active" : "Inactive")} ({killSwitchControl.GlobalLevel}) {killSwitchControl.GlobalReasonCode} - {killSwitchControl.GlobalReason}");
            Console.WriteLine($"OpenOrders: total={openOrdersTotal} pending={openPending.TotalCount} open={openOpen.TotalCount} partial={openPartial.TotalCount}");
            Console.WriteLine($"Positions(non-zero): {nonZeroPositions.Count}");
            foreach (var item in merged)
            {
                Console.WriteLine($"- {item.StrategyId} ({item.Name}): {item.State}, Desired={item.DesiredState}, Enabled={item.Enabled}, Version={item.ConfigVersion}");
            }
        }

        return 0;
    }
}
