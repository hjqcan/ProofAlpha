// ============================================================================
// Export 命令处理器
// ============================================================================

using System.Text.Json;
using System.Globalization;
using Autotrade.Strategy.Application.Contract.Strategies;
using Autotrade.Strategy.Application.Audit;
using Autotrade.Strategy.Application.Decisions;
using Autotrade.Strategy.Application.Promotion;
using Autotrade.Strategy.Application.RunReports;
using Autotrade.Trading.Application.Contract.Repositories;
using Autotrade.Cli.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Autotrade.Cli.Commands;

/// <summary>
/// 处理 export 相关命令：decisions, orders, trades, pnl。
/// </summary>
public static class ExportCommands
{
    /// <summary>
    /// 导出策略决策日志。
    /// </summary>
    public static async Task<int> ExportDecisionsAsync(
        CommandContext context,
        string? strategyId,
        string? marketId,
        string? from,
        string? to,
        int limit,
        FileInfo? output)
    {
        using var scope = context.Host.Services.CreateScope();
        var queryService = scope.ServiceProvider.GetRequiredService<IStrategyDecisionQueryService>();

        // 解析时间范围参数
        DateTimeOffset? fromUtc = null;
        DateTimeOffset? toUtc = null;

        if (!string.IsNullOrWhiteSpace(from))
        {
            if (!DateTimeOffset.TryParse(from, out var parsed))
            {
                Console.Error.WriteLine("无法解析 --from 时间");
                return ExitCodes.ValidationFailed;
            }
            fromUtc = parsed;
        }

        if (!string.IsNullOrWhiteSpace(to))
        {
            if (!DateTimeOffset.TryParse(to, out var parsed))
            {
                Console.Error.WriteLine("无法解析 --to 时间");
                return ExitCodes.ValidationFailed;
            }
            toUtc = parsed;
        }

        // 执行查询
        var query = new StrategyDecisionQuery(strategyId, marketId, fromUtc, toUtc, limit);
        var items = await queryService.QueryRecordsAsync(query).ConfigureAwait(false);

        // 格式化输出
        var payload = context.JsonOutput
            ? JsonSerializer.Serialize(items, new JsonSerializerOptions { WriteIndented = true })
            : FormatDecisionsCsv(items);

        return await WriteOutputAsync(context, payload, output).ConfigureAwait(false);
    }

    /// <summary>
    /// 导出订单记录。
    /// </summary>
    public static async Task<int> ExportOrdersAsync(
        CommandContext context,
        string? strategyId,
        string? marketId,
        string? from,
        string? to,
        int page,
        int pageSize,
        FileInfo? output)
    {
        using var scope = context.Host.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IOrderRepository>();

        var (fromUtc, toUtc) = ParseTimeRange(from, to);
        if (fromUtc is null && !string.IsNullOrWhiteSpace(from))
        {
            Console.Error.WriteLine("无法解析 --from 时间");
            return ExitCodes.ValidationFailed;
        }
        if (toUtc is null && !string.IsNullOrWhiteSpace(to))
        {
            Console.Error.WriteLine("无法解析 --to 时间");
            return ExitCodes.ValidationFailed;
        }

        var result = await repository.GetPagedAsync(page, pageSize, strategyId, marketId, null, fromUtc, toUtc)
            .ConfigureAwait(false);

        var payload = context.JsonOutput
            ? JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true })
            : FormatOrdersCsv(result.Items);

        return await WriteOutputAsync(context, payload, output).ConfigureAwait(false);
    }

    /// <summary>
    /// 导出成交记录。
    /// </summary>
    public static async Task<int> ExportTradesAsync(
        CommandContext context,
        string? strategyId,
        string? marketId,
        string? from,
        string? to,
        int page,
        int pageSize,
        FileInfo? output)
    {
        using var scope = context.Host.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<ITradeRepository>();

        var (fromUtc, toUtc) = ParseTimeRange(from, to);
        if (fromUtc is null && !string.IsNullOrWhiteSpace(from))
        {
            Console.Error.WriteLine("无法解析 --from 时间");
            return ExitCodes.ValidationFailed;
        }
        if (toUtc is null && !string.IsNullOrWhiteSpace(to))
        {
            Console.Error.WriteLine("无法解析 --to 时间");
            return ExitCodes.ValidationFailed;
        }

        var result = await repository.GetPagedAsync(page, pageSize, strategyId, marketId, fromUtc, toUtc)
            .ConfigureAwait(false);

        var payload = context.JsonOutput
            ? JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true })
            : FormatTradesCsv(result.Items);

        return await WriteOutputAsync(context, payload, output).ConfigureAwait(false);
    }

    /// <summary>
    /// 导出 PnL 汇总。
    /// </summary>
    public static async Task<int> ExportPnLAsync(
        CommandContext context,
        string strategyId,
        string? from,
        string? to,
        FileInfo? output)
    {
        if (string.IsNullOrWhiteSpace(strategyId))
        {
            Console.Error.WriteLine("必须指定 --strategy-id");
            return ExitCodes.ValidationFailed;
        }

        using var scope = context.Host.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<ITradeRepository>();

        var (fromUtc, toUtc) = ParseTimeRange(from, to);
        if (fromUtc is null && !string.IsNullOrWhiteSpace(from))
        {
            Console.Error.WriteLine("无法解析 --from 时间");
            return ExitCodes.ValidationFailed;
        }
        if (toUtc is null && !string.IsNullOrWhiteSpace(to))
        {
            Console.Error.WriteLine("无法解析 --to 时间");
            return ExitCodes.ValidationFailed;
        }

        var summary = await repository.GetPnLSummaryAsync(strategyId, fromUtc, toUtc).ConfigureAwait(false);

        var payload = context.JsonOutput
            ? JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true })
            : FormatPnLCsv(summary);

        return await WriteOutputAsync(context, payload, output).ConfigureAwait(false);
    }

    /// <summary>
    /// 导出订单事件（审计日志）。
    /// </summary>
    public static async Task<int> ExportOrderEventsAsync(
        CommandContext context,
        string? strategyId,
        string? marketId,
        string? from,
        string? to,
        int page,
        int pageSize,
        FileInfo? output)
    {
        using var scope = context.Host.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IOrderEventRepository>();

        var (fromUtc, toUtc) = ParseTimeRange(from, to);
        if (fromUtc is null && !string.IsNullOrWhiteSpace(from))
        {
            Console.Error.WriteLine("无法解析 --from 时间");
            return ExitCodes.ValidationFailed;
        }
        if (toUtc is null && !string.IsNullOrWhiteSpace(to))
        {
            Console.Error.WriteLine("无法解析 --to 时间");
            return ExitCodes.ValidationFailed;
        }

        var result = await repository.GetPagedAsync(page, pageSize, strategyId, marketId, null, fromUtc, toUtc)
            .ConfigureAwait(false);

        var payload = context.JsonOutput
            ? JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true })
            : FormatOrderEventsCsv(result.Items);

        return await WriteOutputAsync(context, payload, output).ConfigureAwait(false);
    }

    public static async Task<int> ExportRunReportAsync(
        CommandContext context,
        Guid sessionId,
        int limit,
        FileInfo? output)
    {
        if (sessionId == Guid.Empty)
        {
            Console.Error.WriteLine("A valid --session-id is required");
            return ExitCodes.ValidationFailed;
        }

        using var scope = context.Host.Services.CreateScope();
        var reportService = scope.ServiceProvider.GetRequiredService<IPaperRunReportService>();
        var report = await reportService.GetAsync(sessionId, limit).ConfigureAwait(false);
        if (report is null)
        {
            Console.Error.WriteLine($"Paper run session was not found: {sessionId}");
            return ExitCodes.NotFound;
        }

        var payload = context.JsonOutput
            ? JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true })
            : PaperRunReportCsvFormatter.Format(report);

        return await WriteOutputAsync(context, payload, output).ConfigureAwait(false);
    }

    public static async Task<int> ExportPromotionChecklistAsync(
        CommandContext context,
        Guid sessionId,
        int limit,
        FileInfo? output)
    {
        if (sessionId == Guid.Empty)
        {
            Console.Error.WriteLine("A valid --session-id is required");
            return ExitCodes.ValidationFailed;
        }

        using var scope = context.Host.Services.CreateScope();
        var checklistService = scope.ServiceProvider.GetRequiredService<IPaperPromotionChecklistService>();
        var checklist = await checklistService.EvaluateAsync(sessionId, limit).ConfigureAwait(false);
        if (checklist is null)
        {
            Console.Error.WriteLine($"Paper run session was not found: {sessionId}");
            return ExitCodes.NotFound;
        }

        var payload = context.JsonOutput
            ? JsonSerializer.Serialize(checklist, new JsonSerializerOptions { WriteIndented = true })
            : PaperPromotionChecklistCsvFormatter.Format(checklist);

        return await WriteOutputAsync(context, payload, output).ConfigureAwait(false);
    }

    public static async Task<int> ExportReplayPackageAsync(
        CommandContext context,
        string? strategyId,
        string? marketId,
        Guid? orderId,
        string? clientOrderId,
        Guid? runSessionId,
        Guid? riskEventId,
        string? correlationId,
        string? from,
        string? to,
        int limit,
        FileInfo? output)
    {
        var (fromUtc, toUtc) = ParseTimeRange(from, to);
        if (fromUtc is null && !string.IsNullOrWhiteSpace(from))
        {
            Console.Error.WriteLine("Unable to parse --from timestamp");
            return ExitCodes.ValidationFailed;
        }

        if (toUtc is null && !string.IsNullOrWhiteSpace(to))
        {
            Console.Error.WriteLine("Unable to parse --to timestamp");
            return ExitCodes.ValidationFailed;
        }

        using var scope = context.Host.Services.CreateScope();
        var exportService = scope.ServiceProvider.GetRequiredService<IReplayExportService>();
        var package = await exportService
            .ExportAsync(new ReplayExportQuery(
                strategyId,
                marketId,
                orderId,
                clientOrderId,
                runSessionId,
                riskEventId,
                correlationId,
                fromUtc,
                toUtc,
                limit))
            .ConfigureAwait(false);

        var payload = JsonSerializer.Serialize(package, new JsonSerializerOptions { WriteIndented = true });
        return await WriteOutputAsync(context, payload, output).ConfigureAwait(false);
    }

    private static (DateTimeOffset?, DateTimeOffset?) ParseTimeRange(string? from, string? to)
    {
        DateTimeOffset? fromUtc = null;
        DateTimeOffset? toUtc = null;

        if (!string.IsNullOrWhiteSpace(from) && DateTimeOffset.TryParse(from, out var parsedFrom))
        {
            fromUtc = parsedFrom;
        }

        if (!string.IsNullOrWhiteSpace(to) && DateTimeOffset.TryParse(to, out var parsedTo))
        {
            toUtc = parsedTo;
        }

        return (fromUtc, toUtc);
    }

    private static async Task<int> WriteOutputAsync(CommandContext context, string payload, FileInfo? output)
    {
        if (output is null)
        {
            Console.WriteLine(payload);
        }
        else
        {
            try
            {
                if (File.Exists(output.FullName))
                {
                    Console.Error.WriteLine($"警告: 输出文件已存在，将覆盖: {output.FullName}");
                    if (!ConfirmationService.Confirm($"确认覆盖文件 '{output.FullName}'?", context.GlobalOptions))
                    {
                        Console.WriteLine("操作已取消。");
                        return ExitCodes.UserCancelled;
                    }
                }

                await File.WriteAllTextAsync(output.FullName, payload).ConfigureAwait(false);
                Console.WriteLine($"已导出到 {output.FullName}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"写入输出文件失败: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        }
        return ExitCodes.Success;
    }

    private static string FormatDecisionsCsv(IReadOnlyList<StrategyDecisionRecord> items)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("timestamp_utc,decision_id,run_session_id,strategy_id,action,market_id,reason,correlation_id,execution_mode,context_json");
        foreach (var d in items)
        {
            sb.AppendLine(string.Join(",",
                d.TimestampUtc.ToString("O", CultureInfo.InvariantCulture),
                d.DecisionId.ToString(),
                d.RunSessionId?.ToString() ?? string.Empty,
                Csv(d.StrategyId),
                Csv(d.Action),
                Csv(d.MarketId),
                Csv(d.Reason),
                Csv(d.CorrelationId),
                Csv(d.ExecutionMode),
                Csv(d.ContextJson)));
        }
        return sb.ToString();
    }

    private static string FormatOrdersCsv(IReadOnlyList<OrderDto> orders)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("created_at_utc,updated_at_utc,order_id,trading_account_id,client_order_id,correlation_id,strategy_id,market_id,token_id,outcome,side,order_type,time_in_force,good_til_date_utc,price,quantity,filled_quantity,status,rejection_reason");
        foreach (var o in orders)
        {
            sb.AppendLine(string.Join(",",
                o.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture),
                o.UpdatedAtUtc.ToString("O", CultureInfo.InvariantCulture),
                o.Id.ToString(),
                o.TradingAccountId.ToString(),
                Csv(o.ClientOrderId),
                Csv(o.CorrelationId),
                Csv(o.StrategyId),
                Csv(o.MarketId),
                Csv(o.TokenId),
                Csv(o.Outcome.ToString()),
                Csv(o.Side.ToString()),
                Csv(o.OrderType.ToString()),
                Csv(o.TimeInForce.ToString()),
                Csv(o.GoodTilDateUtc?.ToString("O", CultureInfo.InvariantCulture)),
                o.Price.ToString(CultureInfo.InvariantCulture),
                o.Quantity.ToString(CultureInfo.InvariantCulture),
                o.FilledQuantity.ToString(CultureInfo.InvariantCulture),
                Csv(o.Status.ToString()),
                Csv(o.RejectionReason)));
        }
        return sb.ToString();
    }

    private static string FormatTradesCsv(IReadOnlyList<TradeDto> trades)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("created_at_utc,trade_id,order_id,trading_account_id,exchange_trade_id,client_order_id,correlation_id,strategy_id,market_id,token_id,outcome,side,price,quantity,fee,notional");
        foreach (var t in trades)
        {
            sb.AppendLine(string.Join(",",
                t.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture),
                t.Id.ToString(),
                t.OrderId.ToString(),
                t.TradingAccountId.ToString(),
                Csv(t.ExchangeTradeId),
                Csv(t.ClientOrderId),
                Csv(t.CorrelationId),
                Csv(t.StrategyId),
                Csv(t.MarketId),
                Csv(t.TokenId),
                Csv(t.Outcome.ToString()),
                Csv(t.Side.ToString()),
                t.Price.ToString(CultureInfo.InvariantCulture),
                t.Quantity.ToString(CultureInfo.InvariantCulture),
                t.Fee.ToString(CultureInfo.InvariantCulture),
                t.Notional.ToString(CultureInfo.InvariantCulture)));
        }
        return sb.ToString();
    }

    private static string FormatPnLCsv(PnLSummary summary)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("strategy_id,from_utc,to_utc,trade_count,total_buy_notional,total_sell_notional,gross_profit,total_fees,net_profit");
        sb.AppendLine(string.Join(",",
            Csv(summary.StrategyId),
            Csv(summary.From?.ToString("O", CultureInfo.InvariantCulture)),
            Csv(summary.To?.ToString("O", CultureInfo.InvariantCulture)),
            summary.TradeCount.ToString(CultureInfo.InvariantCulture),
            summary.TotalBuyNotional.ToString(CultureInfo.InvariantCulture),
            summary.TotalSellNotional.ToString(CultureInfo.InvariantCulture),
            summary.GrossProfit.ToString(CultureInfo.InvariantCulture),
            summary.TotalFees.ToString(CultureInfo.InvariantCulture),
            summary.NetProfit.ToString(CultureInfo.InvariantCulture)));
        return sb.ToString();
    }

    private static string FormatOrderEventsCsv(IReadOnlyList<OrderEventDto> events)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("created_at_utc,event_id,run_session_id,order_id,client_order_id,correlation_id,strategy_id,market_id,event_type,status,message,context_json");
        foreach (var e in events)
        {
            sb.AppendLine(string.Join(",",
                e.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture),
                e.Id.ToString(),
                e.RunSessionId?.ToString() ?? string.Empty,
                e.OrderId.ToString(),
                Csv(e.ClientOrderId),
                Csv(e.CorrelationId),
                Csv(e.StrategyId),
                Csv(e.MarketId),
                Csv(e.EventType.ToString()),
                Csv(e.Status.ToString()),
                Csv(e.Message),
                Csv(e.ContextJson)));
        }
        return sb.ToString();
    }

    private static string Csv(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var needsQuote = value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r');
        if (!needsQuote)
        {
            return value;
        }

        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
