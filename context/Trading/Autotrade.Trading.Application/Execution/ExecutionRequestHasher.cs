using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Autotrade.Trading.Application.Contract.Execution;
using Autotrade.Trading.Application.Contract.Repositories;
using Autotrade.Trading.Domain.Shared.Enums;

namespace Autotrade.Trading.Application.Execution;

public static class ExecutionRequestHasher
{
    public static string Compute(ExecutionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return Compute(
            request.TokenId,
            request.MarketId,
            request.Outcome,
            request.Side,
            request.OrderType,
            request.TimeInForce,
            request.NegRisk,
            request.Price,
            request.Quantity,
            request.GoodTilDateUtc);
    }

    public static string Compute(OrderDto order)
    {
        ArgumentNullException.ThrowIfNull(order);

        return Compute(
            order.TokenId,
            order.MarketId,
            order.Outcome,
            order.Side,
            order.OrderType,
            order.TimeInForce,
            order.NegRisk,
            order.Price,
            order.Quantity,
            order.GoodTilDateUtc);
    }

    private static string Compute(
        string? tokenId,
        string marketId,
        OutcomeSide outcome,
        OrderSide side,
        OrderType orderType,
        TimeInForce timeInForce,
        bool negRisk,
        decimal price,
        decimal quantity,
        DateTimeOffset? goodTilDateUtc)
    {
        var json = JsonSerializer.Serialize(new
        {
            TokenId = tokenId,
            MarketId = marketId,
            Outcome = outcome,
            Side = side,
            OrderType = orderType,
            TimeInForce = timeInForce,
            NegRisk = negRisk,
            Price = price,
            Quantity = quantity,
            GoodTilDateUtc = goodTilDateUtc
        });

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(bytes);
    }
}
