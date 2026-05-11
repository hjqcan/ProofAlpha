namespace Autotrade.Trading.Application.Contract.Execution;

/// <summary>
/// 执行结果状态。
/// </summary>
public enum ExecutionStatus
{
    /// <summary>等待提交到交易所。</summary>
    Pending = 1,

    /// <summary>已被交易所接受。</summary>
    Accepted = 2,

    /// <summary>部分成交。</summary>
    PartiallyFilled = 3,

    /// <summary>完全成交。</summary>
    Filled = 4,

    /// <summary>已取消。</summary>
    Cancelled = 5,

    /// <summary>被拒绝。</summary>
    Rejected = 6,

    /// <summary>已过期（GTD）。</summary>
    Expired = 7
}

/// <summary>
/// 执行结果 DTO：封装下单/撤单操作的结果。
/// </summary>
public sealed record ExecutionResult
{
    /// <summary>
    /// 是否成功。
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// 客户端订单 ID。
    /// </summary>
    public required string ClientOrderId { get; init; }

    /// <summary>
    /// 交易所订单 ID（成功时返回）。
    /// </summary>
    public string? ExchangeOrderId { get; init; }

    /// <summary>
    /// 执行状态。
    /// </summary>
    public ExecutionStatus Status { get; init; }

    /// <summary>
    /// 已成交数量。
    /// </summary>
    public decimal FilledQuantity { get; init; }

    /// <summary>
    /// 成交均价（如有）。
    /// </summary>
    public decimal? AveragePrice { get; init; }

    /// <summary>
    /// 错误代码（失败时）。
    /// </summary>
    public string? ErrorCode { get; init; }

    /// <summary>
    /// 错误消息（失败时）。
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// 时间戳（UTC）。
    /// </summary>
    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// 创建成功结果。
    /// </summary>
    public static ExecutionResult Succeed(
        string clientOrderId,
        string exchangeOrderId,
        ExecutionStatus status = ExecutionStatus.Accepted,
        decimal filledQuantity = 0m,
        decimal? averagePrice = null)
    {
        return new ExecutionResult
        {
            Success = true,
            ClientOrderId = clientOrderId,
            ExchangeOrderId = exchangeOrderId,
            Status = status,
            FilledQuantity = filledQuantity,
            AveragePrice = averagePrice
        };
    }

    /// <summary>
    /// 创建失败结果。
    /// </summary>
    public static ExecutionResult Fail(
        string clientOrderId,
        string errorCode,
        string errorMessage,
        ExecutionStatus status = ExecutionStatus.Rejected)
    {
        return new ExecutionResult
        {
            Success = false,
            ClientOrderId = clientOrderId,
            Status = status,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage
        };
    }
}
