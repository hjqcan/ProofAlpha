using Autotrade.Trading.Domain.Entities;
using Autotrade.Trading.Domain.Shared.Enums;
using Autotrade.Trading.Domain.Shared.ValueObjects;

namespace Autotrade.Testing.Builders;

public static class TradingTestData
{
    public static TradingAccount NewTradingAccount(
        string walletAddress = "0xTestWallet",
        decimal totalCapital = 1000m,
        decimal availableCapital = 1000m) =>
        new(walletAddress, totalCapital, availableCapital);

    public static Order NewLimitOrder(
        Guid tradingAccountId,
        string marketId = "test-market",
        OutcomeSide outcome = OutcomeSide.Yes,
        OrderSide side = OrderSide.Buy,
        decimal price = 0.5m,
        decimal quantity = 10m,
        TimeInForce tif = TimeInForce.Gtc) =>
        new(
            tradingAccountId: tradingAccountId,
            marketId: marketId,
            outcome: outcome,
            side: side,
            orderType: OrderType.Limit,
            timeInForce: tif,
            price: new Price(price),
            quantity: new Quantity(quantity),
            goodTilDateUtc: tif == TimeInForce.Gtd ? DateTimeOffset.UtcNow.AddMinutes(5) : null);

    public static Position NewPosition(
        Guid tradingAccountId,
        string marketId = "test-market",
        OutcomeSide outcome = OutcomeSide.Yes) =>
        new(tradingAccountId, marketId, outcome);
}

