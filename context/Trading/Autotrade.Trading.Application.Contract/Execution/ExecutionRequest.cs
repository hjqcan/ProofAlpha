using System.ComponentModel.DataAnnotations;
using Autotrade.Trading.Domain.Shared.Enums;

namespace Autotrade.Trading.Application.Contract.Execution;

/// <summary>
/// 执行请求 DTO：封装下单所需的全部参数。
/// </summary>
public sealed record ExecutionRequest
{
    /// <summary>
    /// 客户端订单 ID（唯一，用于幂等性跟踪）。
    /// </summary>
    [Required]
    public required string ClientOrderId { get; init; }

    /// <summary>
    /// 策略 ID（用于审计）。
    /// </summary>
    public string? StrategyId { get; init; }

    /// <summary>
    /// 关联 ID（用于跨服务跟踪）。
    /// </summary>
    public string? CorrelationId { get; init; }

    /// <summary>
    /// 市场 ID（Condition ID）。
    /// </summary>
    [Required]
    public required string MarketId { get; init; }

    /// <summary>
    /// Token ID（资产 ID）。
    /// </summary>
    [Required]
    public required string TokenId { get; init; }

    /// <summary>
    /// 结果方向（Yes/No）。
    /// </summary>
    public required OutcomeSide Outcome { get; init; }

    /// <summary>
    /// 买卖方向。
    /// </summary>
    public required OrderSide Side { get; init; }

    /// <summary>
    /// 订单类型。
    /// </summary>
    public required OrderType OrderType { get; init; }

    /// <summary>
    /// 订单时效。
    /// </summary>
    public required TimeInForce TimeInForce { get; init; }

    /// <summary>
    /// 价格（0.01 ~ 0.99）。
    /// </summary>
    [Range(0.01, 0.99)]
    public required decimal Price { get; init; }

    /// <summary>
    /// 数量（必须大于 0）。
    /// </summary>
    [Range(0.000001, double.MaxValue)]
    public required decimal Quantity { get; init; }

    /// <summary>
    /// GTD 到期时间（仅当 TimeInForce = GTD 时需要）。
    /// </summary>
    public DateTimeOffset? GoodTilDateUtc { get; init; }

    /// <summary>
    /// 是否为 neg_risk 市场。
    /// </summary>
    public bool NegRisk { get; init; }

    /// <summary>
    /// 验证请求参数。
    /// </summary>
    /// <returns>验证结果，成功返回 null，失败返回错误消息。</returns>
    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(ClientOrderId))
        {
            return "ClientOrderId 不能为空";
        }

        if (string.IsNullOrWhiteSpace(MarketId))
        {
            return "MarketId 不能为空";
        }

        if (string.IsNullOrWhiteSpace(TokenId))
        {
            return "TokenId 不能为空";
        }

        if (Price is < 0.01m or > 0.99m)
        {
            return $"Price 必须在 0.01 ~ 0.99 之间，当前值: {Price}";
        }

        if (Quantity <= 0m)
        {
            return $"Quantity 必须大于 0，当前值: {Quantity}";
        }

        if (TimeInForce == TimeInForce.Gtd)
        {
            if (GoodTilDateUtc is null)
            {
                return "GTD 订单必须提供 GoodTilDateUtc";
            }

            if (GoodTilDateUtc <= DateTimeOffset.UtcNow)
            {
                return "GoodTilDateUtc 必须晚于当前时间";
            }
        }

        // Polymarket CLOB 仅支持限价单
        if (OrderType == OrderType.Market)
        {
            return "Polymarket 不支持市价单，请使用限价单 (Limit)";
        }

        return null;
    }
}
