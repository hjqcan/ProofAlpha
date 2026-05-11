using System.Collections.Generic;
using NetDevPack.Domain;

namespace Autotrade.MarketData.Domain.Shared.ValueObjects;

/// <summary>
/// 数量（份额数量）。
/// </summary>
public sealed class Quantity : ValueObject
{
    // EF Core
    private Quantity()
    {
        Value = 0m;
    }

    public Quantity(decimal value)
    {
        if (value < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "数量不能为负数");
        }

        Value = value;
    }

    public decimal Value { get; }

    public static Quantity Zero { get; } = new(0m);

    public Quantity Add(Quantity other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return new Quantity(Value + other.Value);
    }

    public Quantity Subtract(Quantity other)
    {
        ArgumentNullException.ThrowIfNull(other);

        if (other.Value > Value)
        {
            throw new InvalidOperationException("数量不足，无法扣减");
        }

        return new Quantity(Value - other.Value);
    }

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString()
    {
        return Value.ToString("0.##########");
    }
}
