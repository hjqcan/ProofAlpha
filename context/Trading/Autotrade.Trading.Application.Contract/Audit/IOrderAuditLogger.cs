using Autotrade.Trading.Application.Contract.Repositories;
using Autotrade.Trading.Domain.Shared.Enums;

namespace Autotrade.Trading.Application.Contract.Audit;

/// <summary>
/// 订单审计日志记录器接口。
/// </summary>
public interface IOrderAuditLogger
{
    /// <summary>
    /// 记录订单创建事件。
    /// </summary>
    Task LogOrderCreatedAsync(
        Guid orderId,
        string clientOrderId,
        string strategyId,
        string marketId,
        string? correlationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 记录订单提交事件。
    /// </summary>
    Task LogOrderSubmittedAsync(
        Guid orderId,
        string clientOrderId,
        string strategyId,
        string marketId,
        string? correlationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 记录订单接受事件。
    /// </summary>
    Task LogOrderAcceptedAsync(
        Guid orderId,
        string clientOrderId,
        string strategyId,
        string marketId,
        string? exchangeOrderId,
        string? correlationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 记录订单拒绝事件。
    /// </summary>
    Task LogOrderRejectedAsync(
        Guid orderId,
        string clientOrderId,
        string strategyId,
        string marketId,
        string errorCode,
        string errorMessage,
        string? correlationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 记录订单成交事件。
    /// </summary>
    Task LogOrderFilledAsync(
        Guid orderId,
        string clientOrderId,
        string strategyId,
        string marketId,
        decimal filledQuantity,
        decimal fillPrice,
        bool isPartial,
        string? correlationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 记录订单取消事件。
    /// </summary>
    Task LogOrderCancelledAsync(
        Guid orderId,
        string clientOrderId,
        string strategyId,
        string marketId,
        string? correlationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 记录订单过期事件。
    /// </summary>
    Task LogOrderExpiredAsync(
        Guid orderId,
        string clientOrderId,
        string strategyId,
        string marketId,
        string? correlationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 记录成交。
    /// </summary>
    Task LogTradeAsync(
        Guid orderId,
        Guid tradingAccountId,
        string clientOrderId,
        string strategyId,
        string marketId,
        string tokenId,
        OutcomeSide outcome,
        OrderSide side,
        decimal price,
        decimal quantity,
        string exchangeTradeId,
        decimal fee,
        string? correlationId,
        CancellationToken cancellationToken = default);
}
