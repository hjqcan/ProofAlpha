namespace Autotrade.Trading.Application.Execution;

/// <summary>
/// CLOB order signing inputs that must be replayed unchanged for uncertain-submit retries.
/// </summary>
public sealed record OrderSigningPayload(string Salt, string Timestamp);

/// <summary>
/// 订单跟踪条目：记录客户端订单 ID 到交易所订单 ID 的映射。
/// </summary>
public sealed record OrderTrackingEntry
{
    /// <summary>
    /// 客户端订单 ID。
    /// </summary>
    public required string ClientOrderId { get; init; }

    /// <summary>
    /// 交易所订单 ID（成功提交后设置）。
    /// </summary>
    public string? ExchangeOrderId { get; set; }

    /// <summary>
    /// 市场 ID（用于撤单时更新状态）。
    /// </summary>
    public string? MarketId { get; set; }

    /// <summary>
    /// Token ID（用于撤单时更新状态）。
    /// </summary>
    public string? TokenId { get; set; }

    /// <summary>
    /// 策略 ID（用于审计）。
    /// </summary>
    public string? StrategyId { get; set; }

    /// <summary>
    /// 关联 ID（用于审计）。
    /// </summary>
    public string? CorrelationId { get; set; }

    /// <summary>
    /// 请求哈希（用于检测重复但不同的请求）。
    /// </summary>
    public required string RequestHash { get; init; }

    /// <summary>
    /// CLOB V2 order salt used for the signed envelope.
    /// </summary>
    public string? OrderSalt { get; set; }

    /// <summary>
    /// CLOB V2 order creation timestamp in milliseconds.
    /// </summary>
    public string? OrderTimestamp { get; set; }

    /// <summary>
    /// Indicates that a previously submitted order had an uncertain exchange acknowledgement.
    /// </summary>
    public bool IsUncertainSubmit { get; set; }

    /// <summary>
    /// 创建时间（UTC）。
    /// </summary>
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// 过期时间（UTC）。
    /// </summary>
    public DateTimeOffset ExpiresAtUtc { get; init; }

    /// <summary>
    /// 是否已过期。
    /// </summary>
    public bool IsExpired => DateTimeOffset.UtcNow > ExpiresAtUtc;
}

/// <summary>
/// 幂等性存储接口：防止重复订单提交。
/// </summary>
public interface IIdempotencyStore
{
    /// <summary>
    /// 尝试添加新的订单跟踪条目。
    /// </summary>
    /// <param name="clientOrderId">客户端订单 ID。</param>
    /// <param name="requestHash">请求哈希。</param>
    /// <param name="ttl">生存时间。</param>
    /// <returns>
    /// 如果是新条目，返回 (true, null)；
    /// 如果已存在且请求哈希一致，返回 (false, 已存在的交易所订单 ID)；
    /// 如果已存在但请求哈希不一致，抛出 <see cref="IdempotencyConflictException"/>。
    /// </returns>
    Task<(bool IsNew, string? ExistingExchangeOrderId)> TryAddAsync(
        string clientOrderId,
        string requestHash,
        TimeSpan ttl,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 设置交易所订单 ID。
    /// </summary>
    /// <param name="clientOrderId">客户端订单 ID。</param>
    /// <param name="exchangeOrderId">交易所订单 ID。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task SetExchangeOrderIdAsync(
        string clientOrderId,
        string exchangeOrderId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取订单跟踪条目。
    /// </summary>
    /// <param name="clientOrderId">客户端订单 ID。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>订单跟踪条目，不存在或已过期返回 null。</returns>
    Task<OrderTrackingEntry?> GetAsync(
        string clientOrderId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 根据交易所订单 ID 查找客户端订单 ID。
    /// </summary>
    Task<string?> FindClientOrderIdByExchangeIdAsync(
        string exchangeOrderId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 设置市场信息（MarketId/TokenId）用于撤单时状态更新。
    /// </summary>
    /// <param name="clientOrderId">客户端订单 ID。</param>
    /// <param name="marketId">市场 ID。</param>
    /// <param name="tokenId">Token ID。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task SetMarketInfoAsync(
        string clientOrderId,
        string marketId,
        string tokenId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 设置审计相关信息（StrategyId/CorrelationId）。
    /// </summary>
    Task SetAuditInfoAsync(
        string clientOrderId,
        string? strategyId,
        string? correlationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets existing CLOB signing inputs for a prepared order, if present.
    /// </summary>
    Task<OrderSigningPayload?> GetOrderSigningPayloadAsync(
        string clientOrderId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets existing CLOB signing inputs or creates them for a new prepared order.
    /// </summary>
    Task<OrderSigningPayload> GetOrCreateOrderSigningPayloadAsync(
        string clientOrderId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks a prepared order as externally uncertain so a later identical request may replay the same signed envelope.
    /// </summary>
    Task MarkSubmitUncertainAsync(
        string clientOrderId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears the uncertain-submit marker after the submit result is resolved while keeping the idempotency entry.
    /// </summary>
    Task ClearSubmitUncertainAsync(
        string clientOrderId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 从持久化订单恢复幂等/映射条目。
    /// </summary>
    Task SeedAsync(
        OrderTrackingEntry entry,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 移除订单跟踪条目（用于下单失败后清理以允许重试）。
    /// </summary>
    /// <param name="clientOrderId">客户端订单 ID。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>是否成功移除。</returns>
    Task<bool> RemoveAsync(
        string clientOrderId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 幂等性冲突异常：当使用相同客户端订单 ID 但不同请求参数时抛出。
/// </summary>
public sealed class IdempotencyConflictException : Exception
{
    public IdempotencyConflictException(string clientOrderId, string message)
        : base(message)
    {
        ClientOrderId = clientOrderId;
    }

    public string ClientOrderId { get; }
}
