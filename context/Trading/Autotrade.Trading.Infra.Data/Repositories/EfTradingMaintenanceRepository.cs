using Autotrade.Trading.Application.Contract.Repositories;
using Autotrade.Trading.Infra.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace Autotrade.Trading.Infra.Data.Repositories;

/// <summary>
/// Trading 数据维护仓储 EF Core 实现。
/// </summary>
public sealed class EfTradingMaintenanceRepository : ITradingMaintenanceRepository
{
    private readonly TradingContext _context;

    public EfTradingMaintenanceRepository(TradingContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<int> CleanupAsync(
        DateTimeOffset? ordersCutoffUtc,
        DateTimeOffset? tradesCutoffUtc,
        DateTimeOffset? eventsCutoffUtc,
        DateTimeOffset? riskEventsCutoffUtc,
        CancellationToken cancellationToken = default)
    {
        var totalDeleted = 0;

        // 删除过期订单事件
        if (eventsCutoffUtc.HasValue)
        {
            totalDeleted += await _context.OrderEvents
                .Where(e => e.CreatedAtUtc < eventsCutoffUtc.Value)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        // 删除过期成交
        if (tradesCutoffUtc.HasValue)
        {
            totalDeleted += await _context.Trades
                .Where(t => t.CreatedAtUtc < tradesCutoffUtc.Value)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        // 删除过期订单（注意：只删除终态订单，避免删除进行中的订单）
        if (ordersCutoffUtc.HasValue)
        {
            totalDeleted += await _context.Orders
                .Where(o => o.CreatedAtUtc < ordersCutoffUtc.Value &&
                    (o.Status == Domain.Shared.Enums.OrderStatus.Filled ||
                     o.Status == Domain.Shared.Enums.OrderStatus.Cancelled ||
                     o.Status == Domain.Shared.Enums.OrderStatus.Rejected))
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        // 删除过期风控事件日志
        if (riskEventsCutoffUtc.HasValue)
        {
            totalDeleted += await _context.RiskEventLogs
                .Where(r => r.CreatedAtUtc < riskEventsCutoffUtc.Value)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        return totalDeleted;
    }
}
