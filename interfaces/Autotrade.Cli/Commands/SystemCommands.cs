// ============================================================================
// System 命令处理器
// ============================================================================
// 处理系统控制和查询命令：start/stop/pause/resume, positions, orders, pnl
// ============================================================================

using System.Text.Json;
using Autotrade.Cli.Config;
using Autotrade.Cli.Infrastructure;
using Autotrade.Strategy.Application.Contract.Strategies;
using Autotrade.Strategy.Application.Engine;
using Autotrade.Trading.Application.Contract.Repositories;
using Autotrade.Trading.Domain.Shared.Enums;
using Microsoft.Extensions.DependencyInjection;

namespace Autotrade.Cli.Commands;

/// <summary>
/// 处理系统控制和查询命令。
/// </summary>
public static class SystemCommands
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    // =========================================================================
    // 策略控制命令
    // =========================================================================

    /// <summary>
    /// 启动策略。
    /// </summary>
    public static Task<int> StartStrategyAsync(CommandContext ctx, string strategyId, ConfigFileService configService)
    {
        return SetDesiredStateAsync(ctx, strategyId, StrategyState.Running, configService, requireConfirm: false);
    }

    /// <summary>
    /// 停止策略。
    /// </summary>
    public static Task<int> StopStrategyAsync(CommandContext ctx, string strategyId, ConfigFileService configService)
    {
        return SetDesiredStateAsync(ctx, strategyId, StrategyState.Stopped, configService, requireConfirm: true);
    }

    /// <summary>
    /// 暂停策略。
    /// </summary>
    public static Task<int> PauseStrategyAsync(CommandContext ctx, string strategyId, ConfigFileService configService)
    {
        return SetDesiredStateAsync(ctx, strategyId, StrategyState.Paused, configService, requireConfirm: false);
    }

    /// <summary>
    /// 恢复策略。
    /// </summary>
    public static Task<int> ResumeStrategyAsync(CommandContext ctx, string strategyId, ConfigFileService configService)
    {
        return SetDesiredStateAsync(ctx, strategyId, StrategyState.Running, configService, requireConfirm: false);
    }

    private static Task<int> SetDesiredStateAsync(
        CommandContext ctx,
        string strategyId,
        StrategyState desiredState,
        ConfigFileService configService,
        bool requireConfirm)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(configService);

        if (string.IsNullOrWhiteSpace(strategyId))
        {
            OutputFormatter.WriteError("策略 ID 不能为空", "VALIDATION_FAILED", ctx.GlobalOptions, ExitCodes.ValidationFailed);
            return Task.FromResult(ExitCodes.ValidationFailed);
        }

        // Validate strategy id against registrations (best-effort)
        using (var scope = ctx.Host.Services.CreateScope())
        {
            var manager = scope.ServiceProvider.GetService<IStrategyManager>();
            if (manager is not null)
            {
                var exists = manager.GetRegisteredStrategies()
                    .Any(s => string.Equals(s.StrategyId, strategyId, StringComparison.OrdinalIgnoreCase));

                if (!exists)
                {
                    OutputFormatter.WriteError($"未知策略 ID: {strategyId}", "NOT_FOUND", ctx.GlobalOptions, ExitCodes.NotFound);
                    return Task.FromResult(ExitCodes.NotFound);
                }
            }
        }

        if (requireConfirm)
        {
            if (!ConfirmationService.ConfirmDestructive($"strategy set-desired-state --id {strategyId} --state {desiredState}", ctx.GlobalOptions))
            {
                OutputFormatter.WriteError("操作已取消", "USER_CANCELLED", ctx.GlobalOptions, ExitCodes.UserCancelled);
                return Task.FromResult(ExitCodes.UserCancelled);
            }
        }

        var path = $"StrategyEngine:DesiredStates:{strategyId}";
        configService.SetValue(path, desiredState.ToString());

        OutputFormatter.WriteSuccess(
            $"已写入期望状态：{strategyId} -> {desiredState}（运行中的进程会在数秒内自动生效）",
            new { strategyId, desiredState = desiredState.ToString(), configPath = path },
            ctx.GlobalOptions);

        return Task.FromResult(ExitCodes.Success);
    }

    // =========================================================================
    // 查询命令
    // =========================================================================

    /// <summary>
    /// 列出持仓。
    /// </summary>
    public static async Task<int> ListPositionsAsync(CommandContext ctx)
    {
        using var scope = ctx.Host.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IPositionRepository>();
        var positions = await repository.GetNonZeroAsync(CancellationToken.None).ConfigureAwait(false);

        var data = positions.Select(p => new
        {
            p.MarketId,
            p.Outcome,
            p.Quantity,
            AveragePrice = p.AverageCost,
            p.RealizedPnl,
            UpdatedAtUtc = p.UpdatedAtUtc.ToString("O")
        }).ToList();

        if (ctx.JsonOutput)
        {
            Console.WriteLine(JsonSerializer.Serialize(new { count = data.Count, positions = data }, JsonOptions));
        }
        else
        {
            Console.WriteLine($"持仓列表 ({data.Count} 条):");
            foreach (var p in data)
            {
                Console.WriteLine($"  {p.MarketId} {p.Outcome}: {p.Quantity} @ {p.AveragePrice:F4}, PnL: {p.RealizedPnl:+0.00;-0.00}");
            }
        }

        return ExitCodes.Success;
    }

    /// <summary>
    /// 列出订单。
    /// </summary>
    public static async Task<int> ListOrdersAsync(CommandContext ctx, string? strategyId = null, string? status = null)
    {
        using var scope = ctx.Host.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IOrderRepository>();

        IReadOnlyList<OrderDto> orders;

        if (!string.IsNullOrWhiteSpace(strategyId))
        {
            orders = await repository.GetByStrategyIdAsync(strategyId, limit: 100, cancellationToken: CancellationToken.None)
                .ConfigureAwait(false);
        }
        else if (string.Equals(status, "open", StringComparison.OrdinalIgnoreCase))
        {
            // open 包含 Pending、Open 和 PartiallyFilled
            var pending = await repository.GetByStatusAsync(OrderStatus.Pending, limit: 100, cancellationToken: CancellationToken.None)
                .ConfigureAwait(false);
            var open = await repository.GetByStatusAsync(OrderStatus.Open, limit: 100, cancellationToken: CancellationToken.None)
                .ConfigureAwait(false);
            var partiallyFilled = await repository.GetByStatusAsync(OrderStatus.PartiallyFilled, limit: 100, cancellationToken: CancellationToken.None)
                .ConfigureAwait(false);
            orders = pending.Concat(open).Concat(partiallyFilled).ToList();
        }
        else
        {
            var paged = await repository.GetPagedAsync(1, 100, cancellationToken: CancellationToken.None)
                .ConfigureAwait(false);
            orders = paged.Items;
        }

        var data = orders.Select(o => new
        {
            o.ClientOrderId,
            o.StrategyId,
            o.MarketId,
            Side = o.Side.ToString(),
            Status = o.Status.ToString(),
            o.Price,
            OriginalQuantity = o.Quantity,
            o.FilledQuantity,
            CreatedAtUtc = o.CreatedAtUtc.ToString("O")
        }).ToList();

        if (ctx.JsonOutput)
        {
            Console.WriteLine(JsonSerializer.Serialize(new { count = data.Count, orders = data }, JsonOptions));
        }
        else
        {
            Console.WriteLine($"订单列表 ({data.Count} 条):");
            foreach (var o in data)
            {
                Console.WriteLine($"  [{o.Status}] {o.ClientOrderId} {o.Side} {o.FilledQuantity}/{o.OriginalQuantity} @ {o.Price:F4}");
            }
        }

        return ExitCodes.Success;
    }

    /// <summary>
    /// 显示 PnL。
    /// </summary>
    public static async Task<int> ShowPnLAsync(CommandContext ctx, string? strategyId = null, string? from = null, string? to = null)
    {
        using var scope = ctx.Host.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<ITradeRepository>();

        DateTimeOffset? fromUtc = null;
        DateTimeOffset? toUtc = null;

        if (!string.IsNullOrWhiteSpace(from))
        {
            if (DateTimeOffset.TryParse(from, out var parsedFrom))
            {
                fromUtc = parsedFrom;
            }
            else
            {
                OutputFormatter.WriteError("无法解析 --from 时间", "VALIDATION_FAILED", ctx.GlobalOptions, ExitCodes.ValidationFailed);
                return ExitCodes.ValidationFailed;
            }
        }

        if (!string.IsNullOrWhiteSpace(to))
        {
            if (DateTimeOffset.TryParse(to, out var parsedTo))
            {
                toUtc = parsedTo;
            }
            else
            {
                OutputFormatter.WriteError("无法解析 --to 时间", "VALIDATION_FAILED", ctx.GlobalOptions, ExitCodes.ValidationFailed);
                return ExitCodes.ValidationFailed;
            }
        }

        if (!string.IsNullOrWhiteSpace(strategyId))
        {
            // 使用策略级别的 PnL 汇总
            var summary = await repository.GetPnLSummaryAsync(strategyId, fromUtc, toUtc, CancellationToken.None)
                .ConfigureAwait(false);

            if (ctx.JsonOutput)
            {
                Console.WriteLine(JsonSerializer.Serialize(summary, JsonOptions));
            }
            else
            {
                Console.WriteLine($"PnL 汇总 [{strategyId}]:");
                Console.WriteLine($"  总成交数: {summary.TradeCount}");
                Console.WriteLine($"  总买入金额: {summary.TotalBuyNotional:F4}");
                Console.WriteLine($"  总卖出金额: {summary.TotalSellNotional:F4}");
                Console.WriteLine($"  毛利润: {summary.GrossProfit:+0.0000;-0.0000}");
                Console.WriteLine($"  总手续费: {summary.TotalFees:F4}");
                Console.WriteLine($"  净利润: {summary.NetProfit:+0.0000;-0.0000}");
            }
        }
        else
        {
            // 循环分页获取所有成交并汇总
            var allTrades = new List<TradeDto>();
            var page = 1;
            const int pageSize = 1000;
            const int maxTrades = 10000; // 防止无限循环

            while (allTrades.Count < maxTrades)
            {
                var paged = await repository.GetPagedAsync(page, pageSize, from: fromUtc, to: toUtc, cancellationToken: CancellationToken.None)
                    .ConfigureAwait(false);

                allTrades.AddRange(paged.Items);

                if (paged.Items.Count < pageSize || allTrades.Count >= paged.TotalCount)
                {
                    break;
                }

                page++;
            }

            var trades = allTrades;
            var totalBuy = trades.Where(t => t.Side == OrderSide.Buy).Sum(t => t.Notional);
            var totalSell = trades.Where(t => t.Side == OrderSide.Sell).Sum(t => t.Notional);
            var totalFees = trades.Sum(t => t.Fee);
            var grossProfit = totalSell - totalBuy;
            var netProfit = grossProfit - totalFees;

            var warning = trades.Count >= maxTrades
                ? $" (达到 {maxTrades} 条上限，结果可能不完整)"
                : string.Empty;

            if (ctx.JsonOutput)
            {
                Console.WriteLine(JsonSerializer.Serialize(new
                {
                    tradeCount = trades.Count,
                    totalBuyNotional = totalBuy,
                    totalSellNotional = totalSell,
                    grossProfit,
                    totalFees,
                    netProfit,
                    truncated = trades.Count >= maxTrades
                }, JsonOptions));
            }
            else
            {
                Console.WriteLine($"PnL 汇总 (全部策略){warning}:");
                Console.WriteLine($"  总成交数: {trades.Count}");
                Console.WriteLine($"  总买入金额: {totalBuy:F4}");
                Console.WriteLine($"  总卖出金额: {totalSell:F4}");
                Console.WriteLine($"  毛利润: {grossProfit:+0.0000;-0.0000}");
                Console.WriteLine($"  总手续费: {totalFees:F4}");
                Console.WriteLine($"  净利润: {netProfit:+0.0000;-0.0000}");
            }
        }

        return ExitCodes.Success;
    }
}
