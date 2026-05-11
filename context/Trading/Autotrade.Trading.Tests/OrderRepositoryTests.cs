using Autotrade.Domain.Abstractions.EventBus;
using Autotrade.Infra.Data.Core.EventBus;
using Autotrade.Testing.Db;
using Autotrade.Trading.Application.Contract.Repositories;
using Autotrade.Trading.Domain.Entities;
using Autotrade.Trading.Domain.Events;
using Autotrade.Trading.Domain.Shared.Enums;
using Autotrade.Trading.Infra.Data.Repositories;
using Microsoft.EntityFrameworkCore;
using NetDevPack.Messaging;

namespace Autotrade.Trading.Tests;

public sealed class OrderRepositoryTests
{
    [Fact]
    public async Task EfOrderRepository_AddAsync_ShouldPersistOpenStatusAndClientInfo()
    {
        await using var db = new SqliteInMemoryDatabase();
        await using var ctx = TestDbContextFactory.CreateTradingContextSqlite(db.Connection);
        await ctx.Database.EnsureCreatedAsync();

        var account = new TradingAccount("wallet", 1000m, 1000m);
        ctx.TradingAccounts.Add(account);
        await ctx.SaveChangesAsync();

        var repo = new EfOrderRepository(ctx);

        var id = Guid.NewGuid();
        var dto = new OrderDto(
            Id: id,
            TradingAccountId: account.Id,
            MarketId: "m1",
            TokenId: "t1",
            StrategyId: "s1",
            ClientOrderId: "c1",
            ExchangeOrderId: "ex1",
            CorrelationId: "corr-1",
            Outcome: OutcomeSide.Yes,
            Side: OrderSide.Buy,
            OrderType: OrderType.Limit,
            TimeInForce: TimeInForce.Gtc,
            GoodTilDateUtc: null,
            NegRisk: true,
            Price: 0.50m,
            Quantity: 10m,
            FilledQuantity: 0m,
            Status: OrderStatus.Open,
            RejectionReason: null,
            CreatedAtUtc: DateTimeOffset.UtcNow,
            UpdatedAtUtc: DateTimeOffset.UtcNow);

        await repo.AddAsync(dto);

        var saved = await ctx.Orders.AsNoTracking().SingleAsync(o => o.Id == id);
        Assert.Equal(OrderStatus.Open, saved.Status);
        Assert.Equal("c1", saved.ClientOrderId);
        Assert.Equal("ex1", saved.ExchangeOrderId);
        Assert.Equal("s1", saved.StrategyId);
        Assert.Equal("m1", saved.MarketId);
        Assert.Equal("t1", saved.TokenId);
        Assert.True(saved.NegRisk);
        Assert.Equal(10m, saved.Quantity.Value);
        Assert.Equal(0m, saved.FilledQuantity.Value);
    }

    [Fact]
    public async Task EfOrderRepository_UpdateAsync_ShouldApplyFillThenAllowCancel()
    {
        await using var db = new SqliteInMemoryDatabase();
        await using var ctx = TestDbContextFactory.CreateTradingContextSqlite(db.Connection);
        await ctx.Database.EnsureCreatedAsync();

        var account = new TradingAccount("wallet", 1000m, 1000m);
        ctx.TradingAccounts.Add(account);
        await ctx.SaveChangesAsync();

        var repo = new EfOrderRepository(ctx);

        var id = Guid.NewGuid();
        var baseDto = new OrderDto(
            Id: id,
            TradingAccountId: account.Id,
            MarketId: "m1",
            TokenId: "t1",
            StrategyId: "s1",
            ClientOrderId: "c1",
            ExchangeOrderId: "ex1",
            CorrelationId: "corr-1",
            Outcome: OutcomeSide.Yes,
            Side: OrderSide.Buy,
            OrderType: OrderType.Limit,
            TimeInForce: TimeInForce.Gtc,
            GoodTilDateUtc: null,
            NegRisk: false,
            Price: 0.50m,
            Quantity: 10m,
            FilledQuantity: 0m,
            Status: OrderStatus.Open,
            RejectionReason: null,
            CreatedAtUtc: DateTimeOffset.UtcNow,
            UpdatedAtUtc: DateTimeOffset.UtcNow);

        await repo.AddAsync(baseDto);

        // partial fill
        var partial = baseDto with { FilledQuantity = 5m, Status = OrderStatus.PartiallyFilled };
        await repo.UpdateAsync(partial);

        var afterPartial = await ctx.Orders.AsNoTracking().SingleAsync(o => o.Id == id);
        Assert.Equal(OrderStatus.PartiallyFilled, afterPartial.Status);
        Assert.Equal(5m, afterPartial.FilledQuantity.Value);

        // cancel after partial fill
        var cancelled = baseDto with { FilledQuantity = 5m, Status = OrderStatus.Cancelled };
        await repo.UpdateAsync(cancelled);

        var afterCancel = await ctx.Orders.AsNoTracking().SingleAsync(o => o.Id == id);
        Assert.Equal(OrderStatus.Cancelled, afterCancel.Status);
        Assert.Equal(5m, afterCancel.FilledQuantity.Value);
    }

    [Fact]
    public async Task EfOrderRepository_UpdateAsync_ShouldPublishDomainEventsWhenRepositoryCallsSaveChanges()
    {
        await using var db = new SqliteInMemoryDatabase();
        var publisher = new CapturingIntegrationEventPublisher();
        await using var ctx = TestDbContextFactory.CreateTradingContextSqlite(db.Connection, publisher);
        await ctx.Database.EnsureCreatedAsync();

        var account = new TradingAccount("wallet", 1000m, 1000m);
        ctx.TradingAccounts.Add(account);
        await ctx.SaveChangesAsync();

        var repo = new EfOrderRepository(ctx);
        var dto = NewDto(account.Id, "c-savechanges-dispatch", "ex-savechanges-dispatch");

        await repo.AddAsync(dto);
        publisher.PublishedEvents.Clear();

        await repo.UpdateAsync(dto with { Status = OrderStatus.Cancelled });

        var cancelledEvent = Assert.Single(publisher.PublishedEvents.OfType<OrderCancelledEvent>());
        Assert.Equal(dto.Id, cancelledEvent.AggregateId);
        Assert.Equal(dto.ClientOrderId, cancelledEvent.ClientOrderId);
    }

    [Fact]
    public async Task EfOrderRepository_UpdateAsync_ShouldKeepChangesPendingWhenTransactionalPublishFails()
    {
        await using var db = new SqliteInMemoryDatabase();
        var publisher = new CapturingIntegrationEventPublisher();
        await using var ctx = TestDbContextFactory.CreateTradingContextSqlite(db.Connection, publisher);
        await ctx.Database.EnsureCreatedAsync();

        var account = new TradingAccount("wallet", 1000m, 1000m);
        ctx.TradingAccounts.Add(account);
        await ctx.SaveChangesAsync();

        var repo = new EfOrderRepository(ctx);
        var dto = NewDto(account.Id, "c-savechanges-retry", "ex-savechanges-retry");

        await repo.AddAsync(dto);
        publisher.PublishedEvents.Clear();
        publisher.FailAfterSave = true;

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => repo.UpdateAsync(dto with { Status = OrderStatus.Cancelled }));

        var trackedOrder = await ctx.Orders.SingleAsync(o => o.Id == dto.Id);
        Assert.Equal(OrderStatus.Cancelled, trackedOrder.Status);
        Assert.Equal(EntityState.Modified, ctx.Entry(trackedOrder).State);
        Assert.Contains(trackedOrder.DomainEvents!, domainEvent => domainEvent is OrderCancelledEvent);
        Assert.Empty(publisher.PublishedEvents);

        var persistedAfterRollback = await ctx.Orders.AsNoTracking().SingleAsync(o => o.Id == dto.Id);
        Assert.Equal(OrderStatus.Open, persistedAfterRollback.Status);

        var retrySavedChanges = await ctx.SaveChangesAsync();

        Assert.True(retrySavedChanges > 0);
        Assert.Equal(EntityState.Unchanged, ctx.Entry(trackedOrder).State);
        Assert.Empty(trackedOrder.DomainEvents!);

        var cancelledEvent = Assert.Single(publisher.PublishedEvents.OfType<OrderCancelledEvent>());
        Assert.Equal(dto.Id, cancelledEvent.AggregateId);

        var persistedAfterRetry = await ctx.Orders.AsNoTracking().SingleAsync(o => o.Id == dto.Id);
        Assert.Equal(OrderStatus.Cancelled, persistedAfterRetry.Status);
    }

    [Fact]
    public async Task EfOrderRepository_AddAsync_ShouldRejectDuplicateNonNullExchangeOrderId()
    {
        await using var db = new SqliteInMemoryDatabase();
        await using var ctx = TestDbContextFactory.CreateTradingContextSqlite(db.Connection);
        await ctx.Database.EnsureCreatedAsync();

        var account = new TradingAccount("wallet", 1000m, 1000m);
        ctx.TradingAccounts.Add(account);
        await ctx.SaveChangesAsync();

        var repo = new EfOrderRepository(ctx);
        var first = NewDto(account.Id, "c1", "ex-duplicate");
        var second = NewDto(account.Id, "c2", "ex-duplicate");

        await repo.AddAsync(first);

        await Assert.ThrowsAsync<DbUpdateException>(() => repo.AddAsync(second));
    }

    [Fact]
    public async Task EfOrderRepository_AddAsync_ShouldAllowMultipleNullExchangeOrderIds()
    {
        await using var db = new SqliteInMemoryDatabase();
        await using var ctx = TestDbContextFactory.CreateTradingContextSqlite(db.Connection);
        await ctx.Database.EnsureCreatedAsync();

        var account = new TradingAccount("wallet", 1000m, 1000m);
        ctx.TradingAccounts.Add(account);
        await ctx.SaveChangesAsync();

        var repo = new EfOrderRepository(ctx);

        await repo.AddAsync(NewDto(account.Id, "c1", null));
        await repo.AddAsync(NewDto(account.Id, "c2", null));

        Assert.Equal(2, await ctx.Orders.CountAsync());
    }

    private static OrderDto NewDto(Guid tradingAccountId, string clientOrderId, string? exchangeOrderId) =>
        new(
            Id: Guid.NewGuid(),
            TradingAccountId: tradingAccountId,
            MarketId: "m1",
            TokenId: "t1",
            StrategyId: "s1",
            ClientOrderId: clientOrderId,
            ExchangeOrderId: exchangeOrderId,
            CorrelationId: "corr-1",
            Outcome: OutcomeSide.Yes,
            Side: OrderSide.Buy,
            OrderType: OrderType.Limit,
            TimeInForce: TimeInForce.Gtc,
            GoodTilDateUtc: null,
            NegRisk: false,
            Price: 0.50m,
            Quantity: 10m,
            FilledQuantity: 0m,
            Status: OrderStatus.Open,
            RejectionReason: null,
            CreatedAtUtc: DateTimeOffset.UtcNow,
            UpdatedAtUtc: DateTimeOffset.UtcNow);

    private sealed class CapturingIntegrationEventPublisher : ITransactionalIntegrationEventPublisher
    {
        public List<Event> PublishedEvents { get; } = new();

        public bool FailAfterSave { get; set; }

        public Task PublishAsync(IEnumerable<Event> domainEvents, CancellationToken cancellationToken = default)
        {
            PublishedEvents.AddRange(domainEvents);
            return Task.CompletedTask;
        }

        public int SaveChangesAndPublish(
            DbContext dbContext,
            IReadOnlyCollection<Event> domainEvents,
            Func<int> saveChanges)
        {
            using var transaction = dbContext.Database.BeginTransaction();

            try
            {
                var savedChanges = saveChanges();
                if (FailAfterSave)
                {
                    FailAfterSave = false;
                    throw new InvalidOperationException("Simulated integration event publish failure.");
                }

                if (savedChanges > 0)
                {
                    PublishAsync(domainEvents).GetAwaiter().GetResult();
                }

                transaction.Commit();
                return savedChanges;
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        public async Task<int> SaveChangesAndPublishAsync(
            DbContext dbContext,
            IReadOnlyCollection<Event> domainEvents,
            Func<CancellationToken, Task<int>> saveChangesAsync,
            CancellationToken cancellationToken = default)
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

            try
            {
                var savedChanges = await saveChangesAsync(cancellationToken);
                if (FailAfterSave)
                {
                    FailAfterSave = false;
                    throw new InvalidOperationException("Simulated integration event publish failure.");
                }

                if (savedChanges > 0)
                {
                    await PublishAsync(domainEvents, cancellationToken);
                }

                await transaction.CommitAsync(cancellationToken);
                return savedChanges;
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        }
    }
}

