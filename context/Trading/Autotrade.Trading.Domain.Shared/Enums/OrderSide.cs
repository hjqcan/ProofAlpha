namespace Autotrade.Trading.Domain.Shared.Enums;

/// <summary>
/// 订单方向（买 / 卖）。
/// 注意：这不是 YES/NO 结果侧；结果侧见 <see cref="OutcomeSide"/>。
/// </summary>
public enum OrderSide
{
    Buy = 1,
    Sell = 2
}

