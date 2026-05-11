using System.Collections.Generic;
using NetDevPack.Domain;

namespace Autotrade.MarketData.Domain.Shared.ValueObjects;

/// <summary>
/// 价格（0~1 美元区间内的概率价格）。
/// </summary>
public sealed class Price : ValueObject
{
    // EF Core
    private Price()
    {
        Value = 0m;
    }

    public Price(decimal value)
    {
        if (value < 0m || value > 1m)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "价格必须在区间 [0, 1] 内");
        }

        Value = value;
    }

    public decimal Value { get; }

    public static Price Zero { get; } = new(0m);

    public static Price One { get; } = new(1m);

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString()
    {
        return Value.ToString("0.##########");
    }
}
