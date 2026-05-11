namespace Autotrade.Trading.Domain.Shared.Enums;

/// <summary>
/// 订单时效（Time In Force）。
/// 说明：PRD 要求具备可扩展支持（FAK/FOK/GTC/GTD）。
/// </summary>
public enum TimeInForce
{
    /// <summary>
    /// Good-Til-Canceled：一直有效，直到撤单。
    /// </summary>
    Gtc = 1,

    /// <summary>
    /// Good-Til-Date：到期自动撤单。
    /// </summary>
    Gtd = 2,

    /// <summary>
    /// Fill-And-Kill：立即成交，未成交部分取消（允许部分成交）。
    /// </summary>
    Fak = 3,

    /// <summary>
    /// Fill-Or-Kill：要么全部成交，要么全部取消（不允许部分成交）。
    /// </summary>
    Fok = 4
}

