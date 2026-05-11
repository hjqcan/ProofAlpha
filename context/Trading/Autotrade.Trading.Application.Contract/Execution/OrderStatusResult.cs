namespace Autotrade.Trading.Application.Contract.Execution;

/// <summary>
/// 订单状态查询结果 DTO。
/// </summary>
public sealed record OrderStatusResult
{
    /// <summary>
    /// 是否找到订单。
    /// </summary>
    public required bool Found { get; init; }

    /// <summary>
    /// 客户端订单 ID。
    /// </summary>
    public required string ClientOrderId { get; init; }

    /// <summary>
    /// 交易所订单 ID。
    /// </summary>
    public string? ExchangeOrderId { get; init; }

    /// <summary>
    /// 执行状态。
    /// </summary>
    public ExecutionStatus Status { get; init; }

    /// <summary>
    /// 原始下单数量。
    /// </summary>
    public decimal OriginalQuantity { get; init; }

    /// <summary>
    /// 已成交数量。
    /// </summary>
    public decimal FilledQuantity { get; init; }

    /// <summary>
    /// 剩余数量。
    /// </summary>
    public decimal RemainingQuantity => OriginalQuantity - FilledQuantity;

    /// <summary>
    /// 下单价格。
    /// </summary>
    public decimal Price { get; init; }

    /// <summary>
    /// 成交均价。
    /// </summary>
    public decimal? AverageFilledPrice { get; init; }

    /// <summary>
    /// 创建时间（UTC）。
    /// </summary>
    public DateTimeOffset? CreatedAtUtc { get; init; }

    /// <summary>
    /// 最后更新时间（UTC）。
    /// </summary>
    public DateTimeOffset? UpdatedAtUtc { get; init; }

    /// <summary>
    /// 错误消息（如查询失败）。
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// 创建"未找到"结果。
    /// </summary>
    public static OrderStatusResult NotFound(string clientOrderId, string? errorMessage = null)
    {
        return new OrderStatusResult
        {
            Found = false,
            ClientOrderId = clientOrderId,
            Status = ExecutionStatus.Rejected,
            ErrorMessage = errorMessage ?? "订单未找到"
        };
    }
}
