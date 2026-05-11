using Autotrade.Trading.Domain.Entities;
using Autotrade.Trading.Domain.Shared.ValueObjects;
using NetDevPack.Messaging;

namespace Autotrade.Trading.Tests;

public class TradingDomainBaseTypesTests
{
    [Fact]
    public void ValueObject_结构相等_应成立()
    {
        var p1 = new Price(0.5m);
        var p2 = new Price(0.5m);
        var p3 = new Price(0.6m);

        Assert.Equal(p1, p2);
        Assert.True(p1 == p2);
        Assert.True(p1 != p3);
    }

    [Fact]
    public void Entity_相同Id_应相等()
    {
        var id = Guid.NewGuid();

        var e1 = new TestEntity { Id = id };
        var e2 = new TestEntity { Id = id };

        Assert.True(e1.Equals(e2));
        Assert.True(e1 == e2);
    }

    [Fact]
    public void DomainEvents_添加与清空_应符合预期()
    {
        var aggregate = new TradingAccount(walletAddress: "demo-wallet", totalCapital: 0m, availableCapital: 0m);

        Assert.Null(aggregate.DomainEvents);

        aggregate.AddDomainEvent(new TestDomainEvent(aggregate.Id));
        Assert.NotNull(aggregate.DomainEvents);
        Assert.Single(aggregate.DomainEvents!);

        aggregate.ClearDomainEvents();
        Assert.NotNull(aggregate.DomainEvents);
        Assert.Empty(aggregate.DomainEvents!);
    }

    private sealed class TestEntity : NetDevPack.Domain.Entity
    {
    }

    private sealed class TestDomainEvent : DomainEvent
    {
        public TestDomainEvent(Guid aggregateId) : base(aggregateId)
        {
        }
    }
}
