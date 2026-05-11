using Autotrade.Application.DTOs;
using Autotrade.Trading.Domain.Shared.Enums;

namespace Autotrade.Trading.Application.Contract.Repositories;

/// <summary>
/// 成交仓储接口。
/// </summary>
public interface ITradeRepository
{
    /// <summary>
    /// 根据 ID 获取成交。
    /// </summary>
    Task<TradeDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// 根据订单 ID 获取成交列表。
    /// </summary>
    Task<IReadOnlyList<TradeDto>> GetByOrderIdAsync(Guid orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 根据客户端订单 ID 获取成交列表。
    /// </summary>
    Task<IReadOnlyList<TradeDto>> GetByClientOrderIdAsync(string clientOrderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 根据交易所成交 ID 获取成交。
    /// </summary>
    Task<TradeDto?> GetByExchangeTradeIdAsync(string exchangeTradeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取策略的成交列表。
    /// </summary>
    Task<IReadOnlyList<TradeDto>> GetByStrategyIdAsync(
        string strategyId,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        int? limit = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取市场的成交列表。
    /// </summary>
    Task<IReadOnlyList<TradeDto>> GetByMarketIdAsync(
        string marketId,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        int? limit = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 分页获取成交列表。
    /// </summary>
    Task<PagedResultDto<TradeDto>> GetPagedAsync(
        int page,
        int pageSize,
        string? strategyId = null,
        string? marketId = null,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 添加成交。
    /// </summary>
    Task AddAsync(TradeDto trade, CancellationToken cancellationToken = default);

    /// <summary>
    /// 批量添加成交。
    /// </summary>
    Task AddRangeAsync(IEnumerable<TradeDto> trades, CancellationToken cancellationToken = default);

    /// <summary>
    /// 删除指定时间之前的成交。
    /// </summary>
    Task<int> DeleteBeforeAsync(DateTimeOffset beforeUtc, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取策略的 PnL 汇总。
    /// </summary>
    Task<PnLSummary> GetPnLSummaryAsync(
        string strategyId,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 成交 DTO。
/// </summary>
public sealed record TradeDto(
    Guid Id,
    Guid OrderId,
    Guid TradingAccountId,
    string ClientOrderId,
    string StrategyId,
    string MarketId,
    string TokenId,
    OutcomeSide Outcome,
    OrderSide Side,
    decimal Price,
    decimal Quantity,
    string ExchangeTradeId,
    decimal Fee,
    string? CorrelationId,
    DateTimeOffset CreatedAtUtc)
{
    public decimal Notional => Price * Quantity;
    public decimal NetNotional => Side == OrderSide.Buy ? Notional + Fee : Notional - Fee;
}

/// <summary>
/// PnL 汇总。
/// </summary>
public sealed record PnLSummary(
    string StrategyId,
    decimal TotalBuyNotional,
    decimal TotalSellNotional,
    decimal TotalFees,
    int TradeCount,
    DateTimeOffset? From,
    DateTimeOffset? To)
{
    public decimal GrossProfit => TotalSellNotional - TotalBuyNotional;
    public decimal NetProfit => GrossProfit - TotalFees;
}
