using System.Reflection;
using System.Net.WebSockets;
using System.Text.Json;
using Autotrade.Application.DTOs;
using Autotrade.MarketData.Application.Contract.OrderBook;
using Autotrade.Polymarket.Abstractions;
using Autotrade.Polymarket.Models;
using Autotrade.Polymarket.Options;
using Autotrade.Trading.Application.Compliance;
using Autotrade.Trading.Application.Contract.Audit;
using Autotrade.Trading.Application.Contract.Compliance;
using Autotrade.Trading.Application.Contract.Execution;
using Autotrade.Trading.Application.Contract.Repositories;
using Autotrade.Trading.Application.Contract.Risk;
using Autotrade.Trading.Application.Contract.UserEvents;
using Autotrade.Trading.Application.Execution;
using Autotrade.Trading.Application.Risk;
using Autotrade.Trading.Application.UserEvents;
using Autotrade.Trading.Domain.Shared.Enums;
using Autotrade.Trading.Infra.BackgroundJobs.Workers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Autotrade.Trading.Tests.Execution;

public sealed class LiveExecutionServiceRegressionTests
{
    private static readonly Guid AccountId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    [Fact]
    public async Task PlaceOrdersAsync_WhenBatchDisabled_UsesSequentialPathAndPreservesOrderLimit()
    {
        var requests = new[] { NewRequest("batch-1"), NewRequest("batch-2") };
        var clobClient = new Mock<IPolymarketClobClient>(MockBehavior.Strict);
        clobClient
            .Setup(client => client.PlaceOrderAsync(
                It.IsAny<OrderRequest>(),
                "batch-1",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(PolymarketApiResult<OrderResponse>.Success(200, new OrderResponse
            {
                Success = true,
                OrderId = "exchange-1",
                Status = "LIVE"
            }));

        var idempotencyStore = NewIdempotencyStore();
        var orderRepository = new InMemoryOrderRepository();
        var stateTracker = new InMemoryOrderStateTracker(NullLogger<InMemoryOrderStateTracker>.Instance);
        var service = NewLiveExecutionService(
            clobClient.Object,
            idempotencyStore,
            orderRepository,
            new RecordingOrderAuditLogger(),
            stateTracker,
            new ExecutionOptions
            {
                Mode = ExecutionMode.Live,
                UseBatchOrders = false,
                MaxBatchOrderSize = 15,
                MaxOpenOrdersPerMarket = 1,
                IdempotencyTtlSeconds = 60
            });

        var results = await service.PlaceOrdersAsync(requests);

        Assert.True(results[0].Success);
        Assert.Equal(ExecutionStatus.Accepted, results[0].Status);
        Assert.False(results[1].Success);
        Assert.Equal("ORDER_LIMIT_EXCEEDED", results[1].ErrorCode);
        clobClient.Verify(
            client => client.PlaceOrderAsync(
                It.IsAny<OrderRequest>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
        clobClient.Verify(
            client => client.PlaceOrdersAsync(
                It.IsAny<IReadOnlyList<PostOrderRequest>>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        Assert.NotNull(await idempotencyStore.GetAsync("batch-1"));
        Assert.Null(await idempotencyStore.GetAsync("batch-2"));
        Assert.Equal(1, stateTracker.GetOpenOrderCount("market-1"));
        Assert.Equal(OrderStatus.Open, (await orderRepository.GetByClientOrderIdAsync("batch-1"))!.Status);
    }

    [Fact]
    public async Task PlaceOrdersAsync_WhenNativeBatchSucceeds_PersistsEachAcceptedOrder()
    {
        var requests = new[] { NewRequest("batch-native-1"), NewRequest("batch-native-2") };
        var clobClient = new Mock<IPolymarketClobClient>(MockBehavior.Strict);
        clobClient
            .Setup(client => client.PlaceOrdersAsync(
                It.Is<IReadOnlyList<PostOrderRequest>>(orders => orders.Count == 2),
                It.Is<string>(key => key.StartsWith("batch:", StringComparison.Ordinal)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(PolymarketApiResult<IReadOnlyList<OrderResponse>>.Success(200, new[]
            {
                new OrderResponse { Success = true, OrderId = "exchange-batch-1", Status = "LIVE" },
                new OrderResponse { Success = true, OrderId = "exchange-batch-2", Status = "LIVE" }
            }));

        var idempotencyStore = NewIdempotencyStore();
        var orderRepository = new InMemoryOrderRepository();
        var stateTracker = new InMemoryOrderStateTracker(NullLogger<InMemoryOrderStateTracker>.Instance);
        var service = NewLiveExecutionService(
            clobClient.Object,
            idempotencyStore,
            orderRepository,
            new RecordingOrderAuditLogger(),
            stateTracker);

        var results = await service.PlaceOrdersAsync(requests);

        Assert.All(results, result => Assert.True(result.Success));
        Assert.Equal("exchange-batch-1", results[0].ExchangeOrderId);
        Assert.Equal("exchange-batch-2", results[1].ExchangeOrderId);
        Assert.Equal(OrderStatus.Open, (await orderRepository.GetByClientOrderIdAsync("batch-native-1"))!.Status);
        Assert.Equal(OrderStatus.Open, (await orderRepository.GetByClientOrderIdAsync("batch-native-2"))!.Status);
        Assert.Equal("exchange-batch-1", (await idempotencyStore.GetAsync("batch-native-1"))!.ExchangeOrderId);
        Assert.Equal("exchange-batch-2", (await idempotencyStore.GetAsync("batch-native-2"))!.ExchangeOrderId);
        Assert.Equal(2, stateTracker.GetOpenOrderCount("market-1"));
        clobClient.Verify(
            client => client.PlaceOrderAsync(
                It.IsAny<OrderRequest>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task PlaceOrdersAsync_WhenNativeBatchPartiallyRejects_DuplicateRejectedKeyDoesNotResubmit()
    {
        var requests = new[] { NewRequest("batch-partial-1"), NewRequest("batch-partial-2") };
        var clobClient = new Mock<IPolymarketClobClient>(MockBehavior.Strict);
        var batchCalls = 0;
        clobClient
            .Setup(client => client.PlaceOrdersAsync(
                It.IsAny<IReadOnlyList<PostOrderRequest>>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Returns((IReadOnlyList<PostOrderRequest> orders, string? idempotencyKey, CancellationToken cancellationToken) =>
            {
                batchCalls++;
                if (batchCalls == 1)
                {
                    Assert.Collection(orders, _ => { }, _ => { });
                    return Task.FromResult(PolymarketApiResult<IReadOnlyList<OrderResponse>>.Success(200, new[]
                    {
                        new OrderResponse { Success = true, OrderId = "exchange-partial-1", Status = "LIVE" },
                        new OrderResponse { Success = false, ErrorMsg = "insufficient balance" }
                    }));
                }

                Assert.Single(orders);
                return Task.FromResult(PolymarketApiResult<IReadOnlyList<OrderResponse>>.Success(200, new[]
                {
                    new OrderResponse { Success = true, OrderId = "exchange-partial-new", Status = "LIVE" }
                }));
            });

        var auditLogger = new RecordingOrderAuditLogger();
        var idempotencyStore = NewIdempotencyStore();
        var orderRepository = new InMemoryOrderRepository();
        var stateTracker = new InMemoryOrderStateTracker(NullLogger<InMemoryOrderStateTracker>.Instance);
        var service = NewLiveExecutionService(
            clobClient.Object,
            idempotencyStore,
            orderRepository,
            auditLogger,
            stateTracker,
            new ExecutionOptions
            {
                Mode = ExecutionMode.Live,
                UseBatchOrders = true,
                MaxBatchOrderSize = 15,
                MaxOpenOrdersPerMarket = 2,
                IdempotencyTtlSeconds = 60
            });

        var results = await service.PlaceOrdersAsync(requests);

        Assert.True(results[0].Success);
        Assert.False(results[1].Success);
        Assert.Equal("ORDER_REJECTED", results[1].ErrorCode);
        Assert.Equal(OrderStatus.Open, (await orderRepository.GetByClientOrderIdAsync("batch-partial-1"))!.Status);
        Assert.Equal(OrderStatus.Rejected, (await orderRepository.GetByClientOrderIdAsync("batch-partial-2"))!.Status);
        Assert.Equal(1, stateTracker.GetOpenOrderCount(requests[0].MarketId));
        Assert.NotNull(await idempotencyStore.GetAsync("batch-partial-1"));
        Assert.NotNull(await idempotencyStore.GetAsync("batch-partial-2"));
        Assert.Contains(auditLogger.Rejections, rejection =>
            rejection.ClientOrderId == "batch-partial-2" &&
            rejection.ErrorCode == "ORDER_REJECTED");

        var retryBatchResults = await service.PlaceOrdersAsync(new[]
        {
            requests[1],
            NewRequest("batch-partial-new")
        });

        Assert.False(retryBatchResults[0].Success);
        Assert.Equal(ExecutionStatus.Rejected, retryBatchResults[0].Status);
        Assert.True(retryBatchResults[1].Success);
        Assert.Equal("exchange-partial-new", retryBatchResults[1].ExchangeOrderId);
        Assert.Equal(2, stateTracker.GetOpenOrderCount(requests[0].MarketId));

        var singleRetry = await service.PlaceOrderAsync(requests[1]);

        Assert.False(singleRetry.Success);
        Assert.Equal(ExecutionStatus.Rejected, singleRetry.Status);
        clobClient.Verify(
            client => client.PlaceOrdersAsync(
                It.IsAny<IReadOnlyList<PostOrderRequest>>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Exactly(2));
        clobClient.Verify(
            client => client.PlaceOrderAsync(
                It.IsAny<OrderRequest>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task PlaceOrdersAsync_WhenNativeBatchTransportFails_MarksPreparedOrdersPendingWithoutSequentialResubmit()
    {
        var requests = new[] { NewRequest("batch-fallback-1"), NewRequest("batch-fallback-2") };
        var clobClient = new Mock<IPolymarketClobClient>(MockBehavior.Strict);
        clobClient
            .Setup(client => client.PlaceOrdersAsync(
                It.IsAny<IReadOnlyList<PostOrderRequest>>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(PolymarketApiResult<IReadOnlyList<OrderResponse>>.Failure(503, "service unavailable", string.Empty));

        var idempotencyStore = NewIdempotencyStore();
        var orderRepository = new InMemoryOrderRepository();
        var service = NewLiveExecutionService(
            clobClient.Object,
            idempotencyStore,
            orderRepository,
            new RecordingOrderAuditLogger());

        var results = await service.PlaceOrdersAsync(requests);

        Assert.All(results, result =>
        {
            Assert.False(result.Success);
            Assert.Equal("API_RESULT_UNCERTAIN", result.ErrorCode);
            Assert.Equal(ExecutionStatus.Pending, result.Status);
        });
        Assert.NotNull(await idempotencyStore.GetAsync("batch-fallback-1"));
        Assert.NotNull(await idempotencyStore.GetAsync("batch-fallback-2"));
        Assert.Equal(OrderStatus.Pending, (await orderRepository.GetByClientOrderIdAsync("batch-fallback-1"))!.Status);
        Assert.Equal(OrderStatus.Pending, (await orderRepository.GetByClientOrderIdAsync("batch-fallback-2"))!.Status);
        clobClient.Verify(
            client => client.PlaceOrdersAsync(
                It.IsAny<IReadOnlyList<PostOrderRequest>>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
        clobClient.Verify(
            client => client.PlaceOrderAsync(
                It.IsAny<OrderRequest>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task PlaceOrdersAsync_WhenNativeBatchReturnsDefinitiveClientRejection_MarksPreparedOrdersRejected()
    {
        var requests = new[] { NewRequest("batch-api-reject-1"), NewRequest("batch-api-reject-2") };
        var clobClient = new Mock<IPolymarketClobClient>(MockBehavior.Strict);
        clobClient
            .Setup(client => client.PlaceOrdersAsync(
                It.Is<IReadOnlyList<PostOrderRequest>>(orders => orders.Count == 2),
                It.Is<string>(key => key.StartsWith("batch:", StringComparison.Ordinal)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(PolymarketApiResult<IReadOnlyList<OrderResponse>>.Failure(
                422,
                "invalid signed order envelopes",
                string.Empty));

        var auditLogger = new RecordingOrderAuditLogger();
        var idempotencyStore = NewIdempotencyStore();
        var orderRepository = new InMemoryOrderRepository();
        var stateTracker = new InMemoryOrderStateTracker(NullLogger<InMemoryOrderStateTracker>.Instance);
        var riskManager = new Mock<IRiskManager>();
        riskManager
            .Setup(manager => manager.RecordOrderErrorAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                "ORDER_REJECTED",
                "invalid signed order envelopes",
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var service = NewLiveExecutionService(
            clobClient.Object,
            idempotencyStore,
            orderRepository,
            auditLogger,
            stateTracker,
            riskManager: riskManager.Object);

        var results = await service.PlaceOrdersAsync(requests);

        Assert.All(results, result =>
        {
            Assert.False(result.Success);
            Assert.Equal("ORDER_REJECTED", result.ErrorCode);
            Assert.Equal(ExecutionStatus.Rejected, result.Status);
        });
        Assert.Equal(OrderStatus.Rejected, (await orderRepository.GetByClientOrderIdAsync("batch-api-reject-1"))!.Status);
        Assert.Equal(OrderStatus.Rejected, (await orderRepository.GetByClientOrderIdAsync("batch-api-reject-2"))!.Status);
        Assert.Equal(0, stateTracker.GetOpenOrderCount("market-1"));
        Assert.Equal(2, auditLogger.Rejections.Count);
        Assert.False((await idempotencyStore.GetAsync("batch-api-reject-1"))!.IsUncertainSubmit);
        Assert.False((await idempotencyStore.GetAsync("batch-api-reject-2"))!.IsUncertainSubmit);
        riskManager.Verify(
            manager => manager.RecordOrderErrorAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                "ORDER_REJECTED",
                "invalid signed order envelopes",
                It.IsAny<CancellationToken>()),
            Times.Exactly(2));
        clobClient.Verify(
            client => client.PlaceOrderAsync(
                It.IsAny<OrderRequest>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Theory]
    [InlineData(404)]
    [InlineData(405)]
    [InlineData(410)]
    [InlineData(501)]
    public async Task PlaceOrdersAsync_WhenNativeBatchEndpointUnavailable_FallsBackSequentiallyAndPersistsEachOrder(int statusCode)
    {
        var requests = new[]
        {
            NewRequest($"batch-unavailable-{statusCode}-1"),
            NewRequest($"batch-unavailable-{statusCode}-2")
        };
        var clobClient = new Mock<IPolymarketClobClient>(MockBehavior.Strict);
        clobClient
            .Setup(client => client.PlaceOrdersAsync(
                It.Is<IReadOnlyList<PostOrderRequest>>(orders => orders.Count == 2),
                It.Is<string>(key => key.StartsWith("batch:", StringComparison.Ordinal)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(PolymarketApiResult<IReadOnlyList<OrderResponse>>.Failure(
                statusCode,
                "batch endpoint unavailable",
                string.Empty));
        clobClient
            .Setup(client => client.PlaceOrderAsync(
                It.IsAny<OrderRequest>(),
                requests[0].ClientOrderId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(PolymarketApiResult<OrderResponse>.Success(200, new OrderResponse
            {
                Success = true,
                OrderId = $"exchange-unavailable-{statusCode}-1",
                Status = "LIVE"
            }));
        clobClient
            .Setup(client => client.PlaceOrderAsync(
                It.IsAny<OrderRequest>(),
                requests[1].ClientOrderId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(PolymarketApiResult<OrderResponse>.Success(200, new OrderResponse
            {
                Success = true,
                OrderId = $"exchange-unavailable-{statusCode}-2",
                Status = "LIVE"
            }));

        var idempotencyStore = NewIdempotencyStore();
        var orderRepository = new InMemoryOrderRepository();
        var stateTracker = new InMemoryOrderStateTracker(NullLogger<InMemoryOrderStateTracker>.Instance);
        var service = NewLiveExecutionService(
            clobClient.Object,
            idempotencyStore,
            orderRepository,
            new RecordingOrderAuditLogger(),
            stateTracker,
            new ExecutionOptions
            {
                Mode = ExecutionMode.Live,
                UseBatchOrders = true,
                MaxBatchOrderSize = 15,
                MaxOpenOrdersPerMarket = 2,
                IdempotencyTtlSeconds = 60
            });

        var results = await service.PlaceOrdersAsync(requests);

        Assert.All(results, result =>
        {
            Assert.True(result.Success);
            Assert.Equal(ExecutionStatus.Accepted, result.Status);
        });
        Assert.Equal($"exchange-unavailable-{statusCode}-1", results[0].ExchangeOrderId);
        Assert.Equal($"exchange-unavailable-{statusCode}-2", results[1].ExchangeOrderId);
        Assert.Equal(OrderStatus.Open, (await orderRepository.GetByClientOrderIdAsync(requests[0].ClientOrderId))!.Status);
        Assert.Equal(OrderStatus.Open, (await orderRepository.GetByClientOrderIdAsync(requests[1].ClientOrderId))!.Status);
        Assert.Equal($"exchange-unavailable-{statusCode}-1", (await idempotencyStore.GetAsync(requests[0].ClientOrderId))!.ExchangeOrderId);
        Assert.Equal($"exchange-unavailable-{statusCode}-2", (await idempotencyStore.GetAsync(requests[1].ClientOrderId))!.ExchangeOrderId);
        Assert.Equal(2, stateTracker.GetOpenOrderCount("market-1"));
        clobClient.Verify(
            client => client.PlaceOrdersAsync(
                It.IsAny<IReadOnlyList<PostOrderRequest>>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
        clobClient.Verify(
            client => client.PlaceOrderAsync(
                It.IsAny<OrderRequest>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task PlaceOrdersAsync_WhenRequestsExceedMaxBatchSize_ChunksNativeBatchCalls()
    {
        var requests = new[] { NewRequest("batch-chunk-1"), NewRequest("batch-chunk-2"), NewRequest("batch-chunk-3") };
        var observedBatchSizes = new List<int>();
        var clobClient = new Mock<IPolymarketClobClient>(MockBehavior.Strict);
        clobClient
            .Setup(client => client.PlaceOrdersAsync(
                It.IsAny<IReadOnlyList<PostOrderRequest>>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Returns((IReadOnlyList<PostOrderRequest> orders, string? idempotencyKey, CancellationToken token) =>
            {
                observedBatchSizes.Add(orders.Count);
                var responses = Enumerable.Range(0, orders.Count)
                    .Select(i => new OrderResponse
                    {
                        Success = true,
                        OrderId = $"exchange-chunk-{observedBatchSizes.Count}-{i}",
                        Status = "LIVE"
                    })
                    .ToArray();
                return Task.FromResult(PolymarketApiResult<IReadOnlyList<OrderResponse>>.Success(200, responses));
            });

        var service = NewLiveExecutionService(
            clobClient.Object,
            NewIdempotencyStore(),
            new InMemoryOrderRepository(),
            new RecordingOrderAuditLogger(),
            optionsOverride: new ExecutionOptions
            {
                Mode = ExecutionMode.Live,
                UseBatchOrders = true,
                MaxBatchOrderSize = 2,
                MaxOpenOrdersPerMarket = 10,
                IdempotencyTtlSeconds = 60
            });

        var results = await service.PlaceOrdersAsync(requests);

        Assert.All(results, result => Assert.True(result.Success));
        Assert.Equal(new[] { 2, 1 }, observedBatchSizes);
    }

    [Fact]
    public async Task PlaceOrderAsync_WhenApiTransportFails_MarksPendingAndKeepsIdempotency()
    {
        var request = NewRequest("single-api-failure");
        var clobClient = new Mock<IPolymarketClobClient>(MockBehavior.Strict);
        clobClient
            .Setup(client => client.PlaceOrderAsync(
                It.IsAny<OrderRequest>(),
                request.ClientOrderId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(PolymarketApiResult<OrderResponse>.Failure(503, "service unavailable", string.Empty));

        var auditLogger = new RecordingOrderAuditLogger();
        var orderRepository = new InMemoryOrderRepository();
        var idempotencyStore = NewIdempotencyStore();
        var service = NewLiveExecutionService(clobClient.Object, idempotencyStore, orderRepository, auditLogger);

        var result = await service.PlaceOrderAsync(request);

        Assert.False(result.Success);
        Assert.Equal("API_RESULT_UNCERTAIN", result.ErrorCode);
        Assert.Equal(ExecutionStatus.Pending, result.Status);
        Assert.Empty(auditLogger.Rejections);
        Assert.Contains(auditLogger.Submissions, submission => submission.ClientOrderId == request.ClientOrderId);
        Assert.Equal(OrderStatus.Pending, (await orderRepository.GetByClientOrderIdAsync(request.ClientOrderId))!.Status);
        var trackingEntry = await idempotencyStore.GetAsync(request.ClientOrderId);
        Assert.NotNull(trackingEntry);
        Assert.True(trackingEntry!.IsUncertainSubmit);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(422)]
    public async Task PlaceOrderAsync_WhenApiReturnsDefinitiveClientRejection_PersistsRejectedAndDoesNotMarkUncertain(int statusCode)
    {
        var request = NewRequest($"single-api-reject-{statusCode}");
        var clobClient = new Mock<IPolymarketClobClient>(MockBehavior.Strict);
        clobClient
            .Setup(client => client.PlaceOrderAsync(
                It.IsAny<OrderRequest>(),
                request.ClientOrderId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(PolymarketApiResult<OrderResponse>.Failure(statusCode, "invalid order envelope", string.Empty));

        var auditLogger = new RecordingOrderAuditLogger();
        var orderRepository = new InMemoryOrderRepository();
        var idempotencyStore = NewIdempotencyStore();
        var stateTracker = new InMemoryOrderStateTracker(NullLogger<InMemoryOrderStateTracker>.Instance);
        var riskManager = new Mock<IRiskManager>();
        riskManager
            .Setup(manager => manager.RecordOrderErrorAsync(
                request.StrategyId ?? "unknown",
                request.ClientOrderId,
                "ORDER_REJECTED",
                "invalid order envelope",
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var service = NewLiveExecutionService(
            clobClient.Object,
            idempotencyStore,
            orderRepository,
            auditLogger,
            stateTracker,
            riskManager: riskManager.Object);

        var result = await service.PlaceOrderAsync(request);

        Assert.False(result.Success);
        Assert.Equal("ORDER_REJECTED", result.ErrorCode);
        Assert.Equal(ExecutionStatus.Rejected, result.Status);
        Assert.Contains(auditLogger.Rejections, rejection =>
            rejection.ClientOrderId == request.ClientOrderId &&
            rejection.ErrorCode == "ORDER_REJECTED");
        Assert.Empty(auditLogger.Submissions);
        Assert.Equal(OrderStatus.Rejected, (await orderRepository.GetByClientOrderIdAsync(request.ClientOrderId))!.Status);
        Assert.Equal(0, stateTracker.GetOpenOrderCount(request.MarketId));
        var trackingEntry = await idempotencyStore.GetAsync(request.ClientOrderId);
        Assert.NotNull(trackingEntry);
        Assert.False(trackingEntry!.IsUncertainSubmit);
        riskManager.Verify(
            manager => manager.RecordOrderErrorAsync(
                request.StrategyId ?? "unknown",
                request.ClientOrderId,
                "ORDER_REJECTED",
                "invalid order envelope",
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task PlaceOrderAsync_WhenUncertainSubmitRetryIsDefinitivelyRejected_ClearsUncertainMarkerAndDoesNotResubmit()
    {
        var request = NewRequest("single-uncertain-then-rejected");
        var clobClient = new Mock<IPolymarketClobClient>(MockBehavior.Strict);
        clobClient
            .SetupSequence(client => client.PlaceOrderAsync(
                It.IsAny<OrderRequest>(),
                request.ClientOrderId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(PolymarketApiResult<OrderResponse>.Failure(503, "service unavailable", string.Empty))
            .ReturnsAsync(PolymarketApiResult<OrderResponse>.Failure(422, "invalid order envelope", string.Empty));

        var auditLogger = new RecordingOrderAuditLogger();
        var orderRepository = new InMemoryOrderRepository();
        var idempotencyStore = NewIdempotencyStore();
        var stateTracker = new InMemoryOrderStateTracker(NullLogger<InMemoryOrderStateTracker>.Instance);
        var service = NewLiveExecutionService(
            clobClient.Object,
            idempotencyStore,
            orderRepository,
            auditLogger,
            stateTracker);

        var first = await service.PlaceOrderAsync(request);

        Assert.False(first.Success);
        Assert.Equal("API_RESULT_UNCERTAIN", first.ErrorCode);
        Assert.Equal(ExecutionStatus.Pending, first.Status);
        Assert.True((await idempotencyStore.GetAsync(request.ClientOrderId))!.IsUncertainSubmit);
        Assert.Equal(OrderStatus.Pending, (await orderRepository.GetByClientOrderIdAsync(request.ClientOrderId))!.Status);

        var second = await service.PlaceOrderAsync(request);

        Assert.False(second.Success);
        Assert.Equal("ORDER_REJECTED", second.ErrorCode);
        Assert.Equal(ExecutionStatus.Rejected, second.Status);
        var resolvedEntry = await idempotencyStore.GetAsync(request.ClientOrderId);
        Assert.NotNull(resolvedEntry);
        Assert.False(resolvedEntry!.IsUncertainSubmit);
        Assert.Equal(OrderStatus.Rejected, (await orderRepository.GetByClientOrderIdAsync(request.ClientOrderId))!.Status);
        Assert.Equal(0, stateTracker.GetOpenOrderCount(request.MarketId));
        Assert.Contains(auditLogger.Rejections, rejection =>
            rejection.ClientOrderId == request.ClientOrderId &&
            rejection.ErrorCode == "ORDER_REJECTED");

        var third = await service.PlaceOrderAsync(request);

        Assert.False(third.Success);
        Assert.Equal("ORDER_REJECTED", third.ErrorCode);
        Assert.Equal(ExecutionStatus.Rejected, third.Status);
        clobClient.Verify(
            client => client.PlaceOrderAsync(
                It.IsAny<OrderRequest>(),
                request.ClientOrderId,
                It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task PlaceOrderAsync_WhenClientThrows_MarksPendingAndKeepsIdempotency()
    {
        var request = NewRequest("single-client-exception");
        var clobClient = new Mock<IPolymarketClobClient>(MockBehavior.Strict);
        clobClient
            .Setup(client => client.PlaceOrderAsync(
                It.IsAny<OrderRequest>(),
                request.ClientOrderId,
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("connection dropped"));

        var orderRepository = new InMemoryOrderRepository();
        var idempotencyStore = NewIdempotencyStore();
        var service = NewLiveExecutionService(
            clobClient.Object,
            idempotencyStore,
            orderRepository,
            new RecordingOrderAuditLogger());

        var result = await service.PlaceOrderAsync(request);

        Assert.False(result.Success);
        Assert.Equal("API_RESULT_UNCERTAIN", result.ErrorCode);
        Assert.Equal(ExecutionStatus.Pending, result.Status);
        Assert.Equal(OrderStatus.Pending, (await orderRepository.GetByClientOrderIdAsync(request.ClientOrderId))!.Status);
        Assert.NotNull(await idempotencyStore.GetAsync(request.ClientOrderId));
    }

    [Fact]
    public async Task PlaceOrderAsync_WhenUncertainSubmitIsRetried_CallsClobAgainAndPersistsExchangeId()
    {
        var request = NewRequest("single-uncertain-retry");
        var clobClient = new Mock<IPolymarketClobClient>(MockBehavior.Strict);
        clobClient
            .SetupSequence(client => client.PlaceOrderAsync(
                It.IsAny<OrderRequest>(),
                request.ClientOrderId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(PolymarketApiResult<OrderResponse>.Failure(503, "service unavailable", string.Empty))
            .ReturnsAsync(PolymarketApiResult<OrderResponse>.Success(200, new OrderResponse
            {
                Success = true,
                OrderId = "exchange-recovered",
                Status = "LIVE"
            }));

        var orderRepository = new InMemoryOrderRepository();
        var idempotencyStore = NewIdempotencyStore();
        var stateTracker = new InMemoryOrderStateTracker(NullLogger<InMemoryOrderStateTracker>.Instance);
        var service = NewLiveExecutionService(
            clobClient.Object,
            idempotencyStore,
            orderRepository,
            new RecordingOrderAuditLogger(),
            stateTracker,
            new ExecutionOptions
            {
                Mode = ExecutionMode.Live,
                UseBatchOrders = true,
                MaxBatchOrderSize = 15,
                MaxOpenOrdersPerMarket = 1,
                IdempotencyTtlSeconds = 60
            });

        var first = await service.PlaceOrderAsync(request);
        var second = await service.PlaceOrderAsync(request);

        Assert.False(first.Success);
        Assert.Equal("API_RESULT_UNCERTAIN", first.ErrorCode);
        Assert.Equal(ExecutionStatus.Pending, first.Status);
        Assert.True(second.Success);
        Assert.Equal("exchange-recovered", second.ExchangeOrderId);
        Assert.Equal(ExecutionStatus.Accepted, second.Status);

        clobClient.Verify(
            client => client.PlaceOrderAsync(
                It.IsAny<OrderRequest>(),
                request.ClientOrderId,
                It.IsAny<CancellationToken>()),
            Times.Exactly(2));

        var tracking = await idempotencyStore.GetAsync(request.ClientOrderId);
        Assert.NotNull(tracking);
        Assert.Equal("exchange-recovered", tracking!.ExchangeOrderId);
        var persisted = await orderRepository.GetByClientOrderIdAsync(request.ClientOrderId);
        Assert.NotNull(persisted);
        Assert.Equal("exchange-recovered", persisted!.ExchangeOrderId);
        Assert.Equal(OrderStatus.Open, persisted.Status);
        Assert.Equal(1, stateTracker.GetOpenOrderCount(request.MarketId));
    }

    [Fact]
    public void ExecutionRequestHasher_Compute_IncludesNegRisk()
    {
        var standard = NewRequest("hash-neg-risk") with { NegRisk = false };
        var negRisk = standard with { NegRisk = true };

        Assert.NotEqual(ExecutionRequestHasher.Compute(standard), ExecutionRequestHasher.Compute(negRisk));
    }

    [Fact]
    public async Task PlaceOrderAsync_WhenPersistedClientOrderHasDifferentRequest_ReturnsIdempotencyConflict()
    {
        var original = NewRequest("persisted-conflict");
        var changed = original with { Price = 0.43m };
        var clobClient = new Mock<IPolymarketClobClient>(MockBehavior.Strict);
        var orderRepository = new InMemoryOrderRepository();
        await orderRepository.AddAsync(NewOrderFromRequest(original, "exchange-existing", OrderStatus.Open));

        var service = NewLiveExecutionService(
            clobClient.Object,
            NewIdempotencyStore(),
            orderRepository,
            new RecordingOrderAuditLogger());

        var result = await service.PlaceOrderAsync(changed);

        Assert.False(result.Success);
        Assert.Equal("IDEMPOTENCY_CONFLICT", result.ErrorCode);
        clobClient.Verify(
            client => client.PlaceOrderAsync(
                It.IsAny<OrderRequest>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task PlaceOrdersAsync_WhenPersistedClientOrderHasDifferentRequest_ReturnsConflictWithoutBatchingIt()
    {
        var original = NewRequest("batch-persisted-conflict");
        var changed = original with { Quantity = 11m };
        var acceptedRequest = NewRequest("batch-persisted-conflict-accepted");
        var orderRepository = new InMemoryOrderRepository();
        await orderRepository.AddAsync(NewOrderFromRequest(original, "exchange-existing", OrderStatus.Open));

        var clobClient = new Mock<IPolymarketClobClient>(MockBehavior.Strict);
        clobClient
            .Setup(client => client.PlaceOrdersAsync(
                It.Is<IReadOnlyList<PostOrderRequest>>(orders => orders.Count == 1),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(PolymarketApiResult<IReadOnlyList<OrderResponse>>.Success(200, new[]
            {
                new OrderResponse { Success = true, OrderId = "exchange-accepted", Status = "LIVE" }
            }));

        var service = NewLiveExecutionService(
            clobClient.Object,
            NewIdempotencyStore(),
            orderRepository,
            new RecordingOrderAuditLogger());

        var results = await service.PlaceOrdersAsync(new[] { changed, acceptedRequest });

        Assert.False(results[0].Success);
        Assert.Equal("IDEMPOTENCY_CONFLICT", results[0].ErrorCode);
        Assert.True(results[1].Success);
        Assert.Equal("exchange-accepted", results[1].ExchangeOrderId);
    }

    [Fact]
    public async Task PlaceOrderAsync_WhenAcceptedDuplicateAndComplianceNowBlocks_ReturnsExistingOrderWithoutMutation()
    {
        var request = NewRequest("single-accepted-duplicate");
        var clobClient = new Mock<IPolymarketClobClient>(MockBehavior.Strict);
        var idempotencyStore = NewIdempotencyStore();
        await idempotencyStore
            .TryAddAsync(request.ClientOrderId, ExecutionRequestHasher.Compute(request), TimeSpan.FromMinutes(5));
        await idempotencyStore
            .SetExchangeOrderIdAsync(request.ClientOrderId, "exchange-existing");

        var orderRepository = new InMemoryOrderRepository();
        await orderRepository.AddAsync(NewOrderFromRequest(request, "exchange-existing", OrderStatus.Open));
        var auditLogger = new RecordingOrderAuditLogger();
        var service = NewLiveExecutionService(
            clobClient.Object,
            idempotencyStore,
            orderRepository,
            auditLogger,
            complianceGuard: new BlockingComplianceGuard());

        var result = await service.PlaceOrderAsync(request);

        Assert.True(result.Success);
        Assert.Equal("exchange-existing", result.ExchangeOrderId);
        Assert.Equal(OrderStatus.Open, (await orderRepository.GetByClientOrderIdAsync(request.ClientOrderId))!.Status);
        Assert.Empty(auditLogger.Rejections);
        clobClient.Verify(
            client => client.PlaceOrderAsync(
                It.IsAny<OrderRequest>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task PlaceOrderAsync_WhenPendingDuplicateIsNotRetryableAndComplianceNowBlocks_ReturnsPendingWithoutMutation()
    {
        var request = NewRequest("single-pending-duplicate");
        var clobClient = new Mock<IPolymarketClobClient>(MockBehavior.Strict);
        var idempotencyStore = NewIdempotencyStore();
        await idempotencyStore
            .TryAddAsync(request.ClientOrderId, ExecutionRequestHasher.Compute(request), TimeSpan.FromMinutes(5));

        var orderRepository = new InMemoryOrderRepository();
        var auditLogger = new RecordingOrderAuditLogger();
        var service = NewLiveExecutionService(
            clobClient.Object,
            idempotencyStore,
            orderRepository,
            auditLogger,
            complianceGuard: new BlockingComplianceGuard());

        var result = await service.PlaceOrderAsync(request);

        Assert.True(result.Success);
        Assert.Equal(ExecutionStatus.Pending, result.Status);
        Assert.Null(await orderRepository.GetByClientOrderIdAsync(request.ClientOrderId));
        Assert.Empty(auditLogger.Rejections);
        clobClient.Verify(
            client => client.PlaceOrderAsync(
                It.IsAny<OrderRequest>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task PlaceOrderAsync_WhenSuccessResponseOmitsExchangeOrderId_MarksPendingAndKeepsIdempotency()
    {
        var request = NewRequest("single-missing-exchange-id");
        var clobClient = new Mock<IPolymarketClobClient>(MockBehavior.Strict);
        clobClient
            .Setup(client => client.PlaceOrderAsync(
                It.IsAny<OrderRequest>(),
                request.ClientOrderId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(PolymarketApiResult<OrderResponse>.Success(200, new OrderResponse
            {
                Success = true,
                OrderId = null,
                Status = "LIVE"
            }));

        var idempotencyStore = NewIdempotencyStore();
        var orderRepository = new InMemoryOrderRepository();
        var auditLogger = new RecordingOrderAuditLogger();
        var service = NewLiveExecutionService(clobClient.Object, idempotencyStore, orderRepository, auditLogger);

        var result = await service.PlaceOrderAsync(request);

        Assert.False(result.Success);
        Assert.Equal("MISSING_EXCHANGE_ORDER_ID", result.ErrorCode);
        Assert.Equal(ExecutionStatus.Pending, result.Status);
        Assert.NotNull(await idempotencyStore.GetAsync(request.ClientOrderId));
        Assert.Equal(OrderStatus.Pending, (await orderRepository.GetByClientOrderIdAsync(request.ClientOrderId))!.Status);
        Assert.Empty(auditLogger.Rejections);
    }

    [Fact]
    public async Task PlaceOrdersAsync_WhenBatchSuccessResponseOmitsExchangeOrderId_MarksPendingAndKeepsIdempotency()
    {
        var request = NewRequest("batch-missing-exchange-id");
        var acceptedRequest = NewRequest("batch-missing-exchange-id-accepted");
        var clobClient = new Mock<IPolymarketClobClient>(MockBehavior.Strict);
        clobClient
            .Setup(client => client.PlaceOrdersAsync(
                It.IsAny<IReadOnlyList<PostOrderRequest>>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(PolymarketApiResult<IReadOnlyList<OrderResponse>>.Success(200, new[]
            {
                new OrderResponse { Success = true, OrderId = null, Status = "LIVE" },
                new OrderResponse { Success = true, OrderId = "exchange-batch-accepted", Status = "LIVE" }
            }));

        var idempotencyStore = NewIdempotencyStore();
        var orderRepository = new InMemoryOrderRepository();
        var auditLogger = new RecordingOrderAuditLogger();
        var service = NewLiveExecutionService(clobClient.Object, idempotencyStore, orderRepository, auditLogger);

        var results = await service.PlaceOrdersAsync(new[] { request, acceptedRequest });

        var result = results[0];
        Assert.False(result.Success);
        Assert.Equal("MISSING_EXCHANGE_ORDER_ID", result.ErrorCode);
        Assert.Equal(ExecutionStatus.Pending, result.Status);
        Assert.NotNull(await idempotencyStore.GetAsync(request.ClientOrderId));
        Assert.Equal(OrderStatus.Pending, (await orderRepository.GetByClientOrderIdAsync(request.ClientOrderId))!.Status);
        Assert.True(results[1].Success);
        Assert.Empty(auditLogger.Rejections);
    }

    [Theory]
    [InlineData("MATCHED")]
    [InlineData("FILLED")]
    public async Task PlaceOrderAsync_WhenPlacementReportsMatchedOrFilled_KeepsOrderOpenForReconciliation(
        string placementStatus)
    {
        var request = NewRequest($"single-{placementStatus.ToLowerInvariant()}");
        var clobClient = new Mock<IPolymarketClobClient>(MockBehavior.Strict);
        clobClient
            .Setup(client => client.PlaceOrderAsync(
                It.IsAny<OrderRequest>(),
                request.ClientOrderId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(PolymarketApiResult<OrderResponse>.Success(200, new OrderResponse
            {
                Success = true,
                OrderId = $"exchange-{placementStatus.ToLowerInvariant()}",
                Status = placementStatus
            }));

        var orderRepository = new InMemoryOrderRepository();
        var idempotencyStore = NewIdempotencyStore();
        var stateTracker = new InMemoryOrderStateTracker(NullLogger<InMemoryOrderStateTracker>.Instance);
        var service = NewLiveExecutionService(
            clobClient.Object,
            idempotencyStore,
            orderRepository,
            new RecordingOrderAuditLogger(),
            stateTracker);

        var result = await service.PlaceOrderAsync(request);

        Assert.True(result.Success);
        Assert.Equal(ExecutionStatus.Accepted, result.Status);
        Assert.Equal(0m, result.FilledQuantity);

        var persisted = await orderRepository.GetByClientOrderIdAsync(request.ClientOrderId);
        Assert.NotNull(persisted);
        Assert.Equal(OrderStatus.Open, persisted!.Status);
        Assert.Equal(0m, persisted.FilledQuantity);

        var openOrder = Assert.Single(stateTracker.GetOpenOrders());
        Assert.Equal(request.ClientOrderId, openOrder.ClientOrderId);
        Assert.Equal(ExecutionStatus.Accepted, openOrder.Status);
        Assert.Equal(0m, openOrder.FilledQuantity);
    }

    [Fact]
    public async Task PlaceOrderAsync_WhenExchangeExplicitlyRejects_DuplicateRejectedKeyDoesNotResubmit()
    {
        var request = NewRequest("single-authoritative-reject");
        var newRequest = NewRequest("single-authoritative-new");
        var clobClient = new Mock<IPolymarketClobClient>(MockBehavior.Strict);
        clobClient
            .Setup(client => client.PlaceOrderAsync(
                It.IsAny<OrderRequest>(),
                request.ClientOrderId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(PolymarketApiResult<OrderResponse>.Success(200, new OrderResponse
            {
                Success = false,
                ErrorMsg = "price too aggressive"
            }));
        clobClient
            .Setup(client => client.PlaceOrderAsync(
                It.IsAny<OrderRequest>(),
                newRequest.ClientOrderId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(PolymarketApiResult<OrderResponse>.Success(200, new OrderResponse
            {
                Success = true,
                OrderId = "exchange-authoritative-new",
                Status = "LIVE"
            }));

        var auditLogger = new RecordingOrderAuditLogger();
        var orderRepository = new InMemoryOrderRepository();
        var idempotencyStore = NewIdempotencyStore();
        var stateTracker = new InMemoryOrderStateTracker(NullLogger<InMemoryOrderStateTracker>.Instance);
        var service = NewLiveExecutionService(
            clobClient.Object,
            idempotencyStore,
            orderRepository,
            auditLogger,
            stateTracker,
            new ExecutionOptions
            {
                Mode = ExecutionMode.Live,
                UseBatchOrders = true,
                MaxBatchOrderSize = 15,
                MaxOpenOrdersPerMarket = 1,
                IdempotencyTtlSeconds = 60
            });

        var result = await service.PlaceOrderAsync(request);

        Assert.False(result.Success);
        Assert.Equal("ORDER_REJECTED", result.ErrorCode);
        Assert.Equal(ExecutionStatus.Rejected, result.Status);
        Assert.Contains(auditLogger.Rejections, rejection =>
            rejection.ClientOrderId == request.ClientOrderId &&
            rejection.ErrorCode == "ORDER_REJECTED");
        Assert.Equal(OrderStatus.Rejected, (await orderRepository.GetByClientOrderIdAsync(request.ClientOrderId))!.Status);
        Assert.Equal(0, stateTracker.GetOpenOrderCount(request.MarketId));
        Assert.NotNull(await idempotencyStore.GetAsync(request.ClientOrderId));

        var duplicate = await service.PlaceOrderAsync(request);

        Assert.False(duplicate.Success);
        Assert.Equal("ORDER_REJECTED", duplicate.ErrorCode);
        Assert.Equal(ExecutionStatus.Rejected, duplicate.Status);
        Assert.Equal(0, stateTracker.GetOpenOrderCount(request.MarketId));

        var newResult = await service.PlaceOrderAsync(newRequest);

        Assert.True(newResult.Success);
        Assert.Equal("exchange-authoritative-new", newResult.ExchangeOrderId);
        Assert.Equal(1, stateTracker.GetOpenOrderCount(request.MarketId));
        clobClient.Verify(
            client => client.PlaceOrderAsync(
                It.IsAny<OrderRequest>(),
                request.ClientOrderId,
                It.IsAny<CancellationToken>()),
            Times.Once);
        clobClient.Verify(
            client => client.PlaceOrderAsync(
                It.IsAny<OrderRequest>(),
                newRequest.ClientOrderId,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task PlaceOrderAsync_WhenComplianceBlocks_DuplicateAfterAllowDoesNotResubmit()
    {
        var request = NewRequest("single-compliance-reject");
        var clobClient = new Mock<IPolymarketClobClient>(MockBehavior.Strict);
        var auditLogger = new RecordingOrderAuditLogger();
        var orderRepository = new InMemoryOrderRepository();
        var idempotencyStore = NewIdempotencyStore();
        var blockingService = NewLiveExecutionService(
            clobClient.Object,
            idempotencyStore,
            orderRepository,
            auditLogger,
            complianceGuard: new BlockingComplianceGuard());

        var blocked = await blockingService.PlaceOrderAsync(request);

        Assert.False(blocked.Success);
        Assert.Equal("COMPLIANCE_BLOCKED", blocked.ErrorCode);
        Assert.Equal(ExecutionStatus.Rejected, blocked.Status);
        Assert.Equal(OrderStatus.Rejected, (await orderRepository.GetByClientOrderIdAsync(request.ClientOrderId))!.Status);
        Assert.NotNull(await idempotencyStore.GetAsync(request.ClientOrderId));

        var allowService = NewLiveExecutionService(
            clobClient.Object,
            idempotencyStore,
            orderRepository,
            auditLogger);

        var duplicate = await allowService.PlaceOrderAsync(request);

        Assert.False(duplicate.Success);
        Assert.Equal("ORDER_REJECTED", duplicate.ErrorCode);
        Assert.Equal(ExecutionStatus.Rejected, duplicate.Status);
        clobClient.Verify(
            client => client.PlaceOrderAsync(
                It.IsAny<OrderRequest>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task PlaceOrderAsync_WhenLiveArmingIsNotActive_BlocksBeforeCallingClob()
    {
        var request = NewRequest("single-live-not-armed");
        var clobClient = new Mock<IPolymarketClobClient>(MockBehavior.Strict);
        var auditLogger = new RecordingOrderAuditLogger();
        var orderRepository = new InMemoryOrderRepository();
        var idempotencyStore = NewIdempotencyStore();
        var riskManager = new Mock<IRiskManager>();
        riskManager
            .Setup(manager => manager.RecordOrderErrorAsync(
                request.StrategyId ?? "unknown",
                request.ClientOrderId,
                "LIVE_NOT_ARMED",
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var service = NewLiveExecutionService(
            clobClient.Object,
            idempotencyStore,
            orderRepository,
            auditLogger,
            riskManager: riskManager.Object,
            liveArmingService: new BlockedLiveArmingService());

        var result = await service.PlaceOrderAsync(request);

        Assert.False(result.Success);
        Assert.Equal("LIVE_NOT_ARMED", result.ErrorCode);
        Assert.Equal(ExecutionStatus.Rejected, result.Status);
        Assert.Null(await idempotencyStore.GetAsync(request.ClientOrderId));
        var persisted = await orderRepository.GetByClientOrderIdAsync(request.ClientOrderId);
        Assert.NotNull(persisted);
        Assert.Equal(OrderStatus.Rejected, persisted!.Status);
        Assert.Contains(auditLogger.Rejections, rejection =>
            rejection.ClientOrderId == request.ClientOrderId &&
            rejection.ErrorCode == "LIVE_NOT_ARMED");
        riskManager.Verify(
            manager => manager.RecordOrderErrorAsync(
                request.StrategyId ?? "unknown",
                request.ClientOrderId,
                "LIVE_NOT_ARMED",
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
        clobClient.Verify(
            client => client.PlaceOrderAsync(
                It.IsAny<OrderRequest>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task PlaceOrdersAsync_WhenLiveArmingIsNotActive_BlocksNativeBatchBeforeSubmit()
    {
        var requests = new[] { NewRequest("batch-live-not-armed-1"), NewRequest("batch-live-not-armed-2") };
        var clobClient = new Mock<IPolymarketClobClient>(MockBehavior.Strict);
        var auditLogger = new RecordingOrderAuditLogger();
        var orderRepository = new InMemoryOrderRepository();
        var idempotencyStore = NewIdempotencyStore();
        var riskManager = new Mock<IRiskManager>();
        riskManager
            .Setup(manager => manager.RecordOrderErrorAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                "LIVE_NOT_ARMED",
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var service = NewLiveExecutionService(
            clobClient.Object,
            idempotencyStore,
            orderRepository,
            auditLogger,
            riskManager: riskManager.Object,
            liveArmingService: new BlockedLiveArmingService());

        var results = await service.PlaceOrdersAsync(requests);

        Assert.All(results, result =>
        {
            Assert.False(result.Success);
            Assert.Equal("LIVE_NOT_ARMED", result.ErrorCode);
            Assert.Equal(ExecutionStatus.Rejected, result.Status);
        });
        Assert.Null(await idempotencyStore.GetAsync(requests[0].ClientOrderId));
        Assert.Null(await idempotencyStore.GetAsync(requests[1].ClientOrderId));
        Assert.Equal(OrderStatus.Rejected, (await orderRepository.GetByClientOrderIdAsync(requests[0].ClientOrderId))!.Status);
        Assert.Equal(OrderStatus.Rejected, (await orderRepository.GetByClientOrderIdAsync(requests[1].ClientOrderId))!.Status);
        Assert.Equal(2, auditLogger.Rejections.Count(rejection => rejection.ErrorCode == "LIVE_NOT_ARMED"));
        clobClient.Verify(
            client => client.PlaceOrdersAsync(
                It.IsAny<IReadOnlyList<PostOrderRequest>>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        clobClient.Verify(
            client => client.PlaceOrderAsync(
                It.IsAny<OrderRequest>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CancelOrderAsync_WhenIdempotencyEntryMissing_UsesPersistedOpenOrderAndReseedsMapping()
    {
        var orderRepository = new InMemoryOrderRepository();
        await orderRepository.AddAsync(NewOrder("cancel-db-fallback", "exchange-cancel-db", OrderStatus.Open));
        var idempotencyStore = NewIdempotencyStore();
        var clobClient = new Mock<IPolymarketClobClient>(MockBehavior.Strict);
        clobClient
            .Setup(client => client.CancelOrderAsync("exchange-cancel-db", It.IsAny<CancellationToken>()))
            .ReturnsAsync(PolymarketApiResult<CancelOrderResponse>.Success(200, new CancelOrderResponse
            {
                Canceled = new[] { "exchange-cancel-db" }
            }));

        var service = NewLiveExecutionService(
            clobClient.Object,
            idempotencyStore,
            orderRepository,
            new RecordingOrderAuditLogger());

        var result = await service.CancelOrderAsync("cancel-db-fallback");

        Assert.True(result.Success);
        Assert.Equal(ExecutionStatus.Cancelled, result.Status);
        Assert.Equal(OrderStatus.Cancelled, (await orderRepository.GetByClientOrderIdAsync("cancel-db-fallback"))!.Status);
        var restored = await idempotencyStore.GetAsync("cancel-db-fallback");
        Assert.NotNull(restored);
        Assert.Equal("exchange-cancel-db", restored!.ExchangeOrderId);
    }

    [Fact]
    public async Task GetOrderStatusAsync_WhenIdempotencyEntryMissing_UsesPersistedOpenOrderAndReseedsMapping()
    {
        var orderRepository = new InMemoryOrderRepository();
        await orderRepository.AddAsync(NewOrder("status-db-fallback", "exchange-status-db", OrderStatus.Open));
        var idempotencyStore = NewIdempotencyStore();
        var clobClient = new Mock<IPolymarketClobClient>(MockBehavior.Strict);
        clobClient
            .Setup(client => client.GetOrderAsync("exchange-status-db", It.IsAny<CancellationToken>()))
            .ReturnsAsync(PolymarketApiResult<OrderInfo>.Success(200, new OrderInfo
            {
                Id = "exchange-status-db",
                Market = "old-market",
                AssetId = "old-token",
                OriginalSize = "10",
                SizeMatched = "4",
                Price = "0.42",
                Status = "LIVE",
                CreatedAt = DateTimeOffset.UtcNow.ToString("O")
            }));

        var service = NewLiveExecutionService(
            clobClient.Object,
            idempotencyStore,
            orderRepository,
            new RecordingOrderAuditLogger());

        var result = await service.GetOrderStatusAsync("status-db-fallback");

        Assert.True(result.Found);
        Assert.Equal("exchange-status-db", result.ExchangeOrderId);
        Assert.Equal(ExecutionStatus.Accepted, result.Status);
        Assert.Equal(4m, result.FilledQuantity);
        var restored = await idempotencyStore.GetAsync("status-db-fallback");
        Assert.NotNull(restored);
        Assert.Equal("exchange-status-db", restored!.ExchangeOrderId);
    }

    [Fact]
    public async Task PlaceOrderAsync_WhenUnsafeLiveParametersAreAllowed_RecordsRiskWarningOnce()
    {
        var request = NewRequest("live-compliance-warning");
        var riskEvents = new InMemoryRiskEventRepository(NullLogger<InMemoryRiskEventRepository>.Instance);
        var clobClient = new Mock<IPolymarketClobClient>(MockBehavior.Strict);
        clobClient
            .Setup(client => client.PlaceOrderAsync(
                It.IsAny<OrderRequest>(),
                request.ClientOrderId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(PolymarketApiResult<OrderResponse>.Success(200, new OrderResponse
            {
                Success = true,
                OrderId = "exchange-live-warning",
                Status = "LIVE"
            }));

        var service = NewLiveExecutionService(
            clobClient.Object,
            NewIdempotencyStore(),
            new InMemoryOrderRepository(),
            new RecordingOrderAuditLogger(),
            complianceGuard: new WarningComplianceGuard(),
            riskEventRepository: riskEvents);

        var first = await service.PlaceOrderAsync(request);
        var second = await service.PlaceOrderAsync(request);

        Assert.True(first.Success);
        Assert.True(second.Success);
        var warnings = await riskEvents.QueryAsync(request.StrategyId);
        var warning = Assert.Single(warnings);
        Assert.Equal("COMPLIANCE_TEST_WARNING", warning.Code);
        Assert.Equal(RiskSeverity.Warning, warning.Severity);
    }

    [Fact]
    public async Task ReconcileOpenOrdersAsync_WhenExchangeReportsFill_UpdatesPersistedOrder()
    {
        var orderRepository = new InMemoryOrderRepository();
        await orderRepository.AddAsync(NewOrder("reconcile-client", "exchange-1", status: OrderStatus.Open));

        var idempotencyStore = NewIdempotencyStore();
        await idempotencyStore.SeedAsync(new OrderTrackingEntry
        {
            ClientOrderId = "reconcile-client",
            ExchangeOrderId = "exchange-1",
            RequestHash = "hash",
            MarketId = "old-market",
            TokenId = "old-token",
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(1)
        });

        var clobClient = new Mock<IPolymarketClobClient>(MockBehavior.Strict);
        clobClient
            .Setup(client => client.GetOpenOrdersAsync(null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(PolymarketApiResult<IReadOnlyList<OrderInfo>>.Success(200, new[]
            {
                new OrderInfo
                {
                    Id = "exchange-1",
                    Market = "market-new",
                    AssetId = "token-new",
                    OriginalSize = "10",
                    SizeMatched = "4",
                    Price = "0.42",
                    Status = "LIVE"
                }
            }));
        clobClient
            .Setup(client => client.GetTradesAsync("market-new", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(PolymarketApiResult<IReadOnlyList<TradeInfo>>.Success(200, Array.Empty<TradeInfo>()));

        var stateTracker = new Mock<IOrderStateTracker>();
        stateTracker.Setup(tracker => tracker.GetOpenOrders()).Returns(Array.Empty<OrderStateUpdate>());
        stateTracker
            .Setup(tracker => tracker.OnOrderStateChangedAsync(It.IsAny<OrderStateUpdate>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        using var provider = NewScopedProvider(orderRepository, new RecordingOrderAuditLogger());
        var worker = new OrderReconciliationWorker(
            clobClient.Object,
            idempotencyStore,
            stateTracker.Object,
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new ExecutionOptions { Mode = ExecutionMode.Live, EnableReconciliation = true }),
            NullLogger<OrderReconciliationWorker>.Instance);

        await InvokePrivateTaskAsync(worker, "ReconcileOpenOrdersAsync", CancellationToken.None);

        var persisted = await orderRepository.GetByClientOrderIdAsync("reconcile-client");
        Assert.NotNull(persisted);
        Assert.Equal(OrderStatus.PartiallyFilled, persisted.Status);
        Assert.Equal(4m, persisted.FilledQuantity);
        Assert.Equal("exchange-1", persisted.ExchangeOrderId);
        Assert.Equal("market-new", persisted.MarketId);
        Assert.Equal("token-new", persisted.TokenId);
    }

    [Fact]
    public async Task ReconcileOpenOrdersAsync_WhenExchangeMapMissing_UsesPersistedExchangeOrderAndReseedsMapping()
    {
        var orderRepository = new InMemoryOrderRepository();
        await orderRepository.AddAsync(NewOrder("reconcile-db-fallback", "exchange-db-fallback", status: OrderStatus.Open));

        var idempotencyStore = NewIdempotencyStore();
        var clobClient = new Mock<IPolymarketClobClient>(MockBehavior.Strict);
        clobClient
            .Setup(client => client.GetOpenOrdersAsync(null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(PolymarketApiResult<IReadOnlyList<OrderInfo>>.Success(200, new[]
            {
                new OrderInfo
                {
                    Id = "exchange-db-fallback",
                    Market = "market-db",
                    AssetId = "token-db",
                    OriginalSize = "10",
                    SizeMatched = "4",
                    Price = "0.42",
                    Status = "LIVE"
                }
            }));
        clobClient
            .Setup(client => client.GetTradesAsync("market-db", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(PolymarketApiResult<IReadOnlyList<TradeInfo>>.Success(200, Array.Empty<TradeInfo>()));

        OrderStateUpdate? observedState = null;
        var stateTracker = new Mock<IOrderStateTracker>();
        stateTracker.Setup(tracker => tracker.GetOpenOrders()).Returns(Array.Empty<OrderStateUpdate>());
        stateTracker
            .Setup(tracker => tracker.OnOrderStateChangedAsync(It.IsAny<OrderStateUpdate>(), It.IsAny<CancellationToken>()))
            .Callback<OrderStateUpdate, CancellationToken>((update, _) => observedState = update)
            .Returns(Task.CompletedTask);

        var auditLogger = new RecordingOrderAuditLogger();
        using var provider = NewScopedProvider(orderRepository, auditLogger);
        var worker = new OrderReconciliationWorker(
            clobClient.Object,
            idempotencyStore,
            stateTracker.Object,
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new ExecutionOptions { Mode = ExecutionMode.Live, EnableReconciliation = true }),
            NullLogger<OrderReconciliationWorker>.Instance);

        await InvokePrivateTaskAsync(worker, "ReconcileOpenOrdersAsync", CancellationToken.None);

        var persisted = await orderRepository.GetByClientOrderIdAsync("reconcile-db-fallback");
        Assert.NotNull(persisted);
        Assert.Equal(OrderStatus.PartiallyFilled, persisted!.Status);
        Assert.Equal(4m, persisted.FilledQuantity);
        Assert.Equal("market-db", persisted.MarketId);
        Assert.Equal("token-db", persisted.TokenId);
        Assert.NotNull(observedState);
        Assert.Equal(ExecutionStatus.PartiallyFilled, observedState!.Status);
        var restored = await idempotencyStore.GetAsync("reconcile-db-fallback");
        Assert.NotNull(restored);
        Assert.Equal("exchange-db-fallback", restored!.ExchangeOrderId);
        Assert.Single(auditLogger.Fills);
    }

    [Fact]
    public async Task ReconcileOpenOrdersAsync_WhenFillIncreases_LogsOnlyFillDelta()
    {
        var orderRepository = new InMemoryOrderRepository();
        await orderRepository.AddAsync(NewOrder(
            "reconcile-delta-client",
            "exchange-delta",
            status: OrderStatus.PartiallyFilled,
            filledQuantity: 2m));

        var idempotencyStore = NewIdempotencyStore();
        await idempotencyStore.SeedAsync(new OrderTrackingEntry
        {
            ClientOrderId = "reconcile-delta-client",
            ExchangeOrderId = "exchange-delta",
            RequestHash = "hash",
            MarketId = "market-1",
            TokenId = "token-1",
            StrategyId = "strategy-1",
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(1)
        });

        var clobClient = new Mock<IPolymarketClobClient>(MockBehavior.Strict);
        clobClient
            .Setup(client => client.GetOpenOrdersAsync(null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(PolymarketApiResult<IReadOnlyList<OrderInfo>>.Success(200, new[]
            {
                new OrderInfo
                {
                    Id = "exchange-delta",
                    Market = "market-1",
                    AssetId = "token-1",
                    OriginalSize = "10",
                    SizeMatched = "4",
                    Price = "0.42",
                    Status = "LIVE"
                }
            }));
        clobClient
            .Setup(client => client.GetTradesAsync("market-1", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(PolymarketApiResult<IReadOnlyList<TradeInfo>>.Success(200, Array.Empty<TradeInfo>()));

        var stateTracker = new Mock<IOrderStateTracker>();
        stateTracker.Setup(tracker => tracker.GetOpenOrders()).Returns(new[]
        {
            new OrderStateUpdate
            {
                ClientOrderId = "reconcile-delta-client",
                ExchangeOrderId = "exchange-delta",
                MarketId = "market-1",
                TokenId = "token-1",
                Status = ExecutionStatus.PartiallyFilled,
                OriginalQuantity = 10m,
                FilledQuantity = 2m
            }
        });
        stateTracker
            .Setup(tracker => tracker.OnOrderStateChangedAsync(It.IsAny<OrderStateUpdate>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var auditLogger = new RecordingOrderAuditLogger();
        using var provider = NewScopedProvider(orderRepository, auditLogger);
        var worker = new OrderReconciliationWorker(
            clobClient.Object,
            idempotencyStore,
            stateTracker.Object,
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new ExecutionOptions { Mode = ExecutionMode.Live, EnableReconciliation = true }),
            NullLogger<OrderReconciliationWorker>.Instance);

        await InvokePrivateTaskAsync(worker, "ReconcileOpenOrdersAsync", CancellationToken.None);

        var fill = Assert.Single(auditLogger.Fills);
        Assert.Equal("reconcile-delta-client", fill.ClientOrderId);
        Assert.Equal(2m, fill.FilledQuantity);
    }

    [Fact]
    public async Task ReconcileOpenOrdersAsync_WhenRestSnapshotHasLowerFill_DoesNotRegressLocalFillOrAuditDelta()
    {
        var orderRepository = new InMemoryOrderRepository();
        await orderRepository.AddAsync(NewOrder(
            "reconcile-stale-client",
            "exchange-stale",
            status: OrderStatus.PartiallyFilled,
            filledQuantity: 4m));

        var idempotencyStore = NewIdempotencyStore();
        await idempotencyStore.SeedAsync(new OrderTrackingEntry
        {
            ClientOrderId = "reconcile-stale-client",
            ExchangeOrderId = "exchange-stale",
            RequestHash = "hash",
            MarketId = "market-1",
            TokenId = "token-1",
            StrategyId = "strategy-1",
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(1)
        });

        var clobClient = new Mock<IPolymarketClobClient>(MockBehavior.Strict);
        clobClient
            .Setup(client => client.GetOpenOrdersAsync(null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(PolymarketApiResult<IReadOnlyList<OrderInfo>>.Success(200, new[]
            {
                new OrderInfo
                {
                    Id = "exchange-stale",
                    Market = "market-1",
                    AssetId = "token-1",
                    OriginalSize = "10",
                    SizeMatched = "2",
                    Price = "0.42",
                    Status = "LIVE"
                }
            }));
        clobClient
            .Setup(client => client.GetTradesAsync("market-1", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(PolymarketApiResult<IReadOnlyList<TradeInfo>>.Success(200, Array.Empty<TradeInfo>()));

        var existingState = new OrderStateUpdate
        {
            ClientOrderId = "reconcile-stale-client",
            ExchangeOrderId = "exchange-stale",
            MarketId = "market-1",
            TokenId = "token-1",
            Status = ExecutionStatus.PartiallyFilled,
            OriginalQuantity = 10m,
            FilledQuantity = 4m
        };
        OrderStateUpdate? observedState = null;
        var stateTracker = new Mock<IOrderStateTracker>();
        stateTracker.Setup(tracker => tracker.GetOpenOrders()).Returns(new[] { existingState });
        stateTracker
            .Setup(tracker => tracker.OnOrderStateChangedAsync(It.IsAny<OrderStateUpdate>(), It.IsAny<CancellationToken>()))
            .Callback<OrderStateUpdate, CancellationToken>((update, _) => observedState = update)
            .Returns(Task.CompletedTask);

        var auditLogger = new RecordingOrderAuditLogger();
        using var provider = NewScopedProvider(orderRepository, auditLogger);
        var worker = new OrderReconciliationWorker(
            clobClient.Object,
            idempotencyStore,
            stateTracker.Object,
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new ExecutionOptions { Mode = ExecutionMode.Live, EnableReconciliation = true }),
            NullLogger<OrderReconciliationWorker>.Instance);

        await InvokePrivateTaskAsync(worker, "ReconcileOpenOrdersAsync", CancellationToken.None);

        var persisted = await orderRepository.GetByClientOrderIdAsync("reconcile-stale-client");
        Assert.NotNull(persisted);
        Assert.Equal(OrderStatus.PartiallyFilled, persisted!.Status);
        Assert.Equal(4m, persisted.FilledQuantity);
        Assert.NotNull(observedState);
        Assert.Equal(ExecutionStatus.PartiallyFilled, observedState!.Status);
        Assert.Equal(4m, observedState.FilledQuantity);
        Assert.Empty(auditLogger.Fills);
    }

    [Fact]
    public async Task ReconcileOpenOrdersAsync_WhenAssociateTradeIsMakerFill_LogsPersistedOrderSide()
    {
        var orderRepository = new InMemoryOrderRepository();
        await orderRepository.AddAsync(NewOrder(
            "reconcile-maker-client",
            "exchange-maker",
            status: OrderStatus.Open) with
        {
            Side = OrderSide.Sell
        });

        var idempotencyStore = NewIdempotencyStore();
        await idempotencyStore.SeedAsync(new OrderTrackingEntry
        {
            ClientOrderId = "reconcile-maker-client",
            ExchangeOrderId = "exchange-maker",
            RequestHash = "hash",
            MarketId = "market-maker",
            TokenId = "token-maker",
            StrategyId = "strategy-1",
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(1)
        });

        var clobClient = new Mock<IPolymarketClobClient>(MockBehavior.Strict);
        clobClient
            .Setup(client => client.GetOpenOrdersAsync(null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(PolymarketApiResult<IReadOnlyList<OrderInfo>>.Success(200, new[]
            {
                new OrderInfo
                {
                    Id = "exchange-maker",
                    Market = "market-maker",
                    AssetId = "token-maker",
                    OriginalSize = "10",
                    SizeMatched = "2",
                    Price = "0.42",
                    Status = "LIVE",
                    AssociateTrades = new[] { "trade-maker" }
                }
            }));
        clobClient
            .Setup(client => client.GetTradesAsync("market-maker", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(PolymarketApiResult<IReadOnlyList<TradeInfo>>.Success(200, new[]
            {
                new TradeInfo
                {
                    Id = "trade-maker",
                    TakerOrderId = "other-taker",
                    Market = "market-maker",
                    AssetId = "token-maker",
                    Side = "BUY",
                    Outcome = "Yes",
                    Size = "2",
                    Price = "0.42",
                    Status = "MATCHED"
                }
            }));

        var stateTracker = new Mock<IOrderStateTracker>();
        stateTracker.Setup(tracker => tracker.GetOpenOrders()).Returns(Array.Empty<OrderStateUpdate>());
        stateTracker
            .Setup(tracker => tracker.OnOrderStateChangedAsync(It.IsAny<OrderStateUpdate>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var auditLogger = new RecordingOrderAuditLogger();
        using var provider = NewScopedProvider(orderRepository, auditLogger);
        var worker = new OrderReconciliationWorker(
            clobClient.Object,
            idempotencyStore,
            stateTracker.Object,
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new ExecutionOptions { Mode = ExecutionMode.Live, EnableReconciliation = true }),
            NullLogger<OrderReconciliationWorker>.Instance);

        await InvokePrivateTaskAsync(worker, "ReconcileOpenOrdersAsync", CancellationToken.None);

        var trade = Assert.Single(auditLogger.Trades);
        Assert.Equal("reconcile-maker-client", trade.ClientOrderId);
        Assert.Equal("trade-maker", trade.ExchangeTradeId);
        Assert.Equal(OrderSide.Sell, trade.Side);
    }

    [Fact]
    public async Task OrderStateRecoveryWorker_WhenPaperMode_DoesNotRestoreHalfState()
    {
        var orderRepository = new InMemoryOrderRepository();
        await orderRepository.AddAsync(NewOrder("paper-open", "paper-exchange", status: OrderStatus.Open));
        var idempotencyStore = NewIdempotencyStore();
        var stateTracker = new InMemoryOrderStateTracker(NullLogger<InMemoryOrderStateTracker>.Instance);
        using var provider = NewScopedProvider(orderRepository, new RecordingOrderAuditLogger());
        var worker = new OrderStateRecoveryWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            idempotencyStore,
            stateTracker,
            Options.Create(new ExecutionOptions { Mode = ExecutionMode.Paper }),
            NullLogger<OrderStateRecoveryWorker>.Instance);

        await worker.StartAsync(CancellationToken.None);

        Assert.Equal(0, stateTracker.GetOpenOrderCount("old-market"));
        Assert.Null(await idempotencyStore.GetAsync("paper-open"));
    }

    [Fact]
    public async Task OrderStateRecoveryWorker_WhenLiveModeRestoresOpenOrder_ReplayReturnsExistingExchangeId()
    {
        var request = NewRequest("recovered-open");
        var orderRepository = new InMemoryOrderRepository();
        await orderRepository.AddAsync(NewOrderFromRequest(request, "exchange-restored", OrderStatus.Open));

        var idempotencyStore = NewIdempotencyStore();
        var stateTracker = new InMemoryOrderStateTracker(NullLogger<InMemoryOrderStateTracker>.Instance);
        using var provider = NewScopedProvider(orderRepository, new RecordingOrderAuditLogger());
        var worker = new OrderStateRecoveryWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            idempotencyStore,
            stateTracker,
            Options.Create(new ExecutionOptions { Mode = ExecutionMode.Live, IdempotencyTtlSeconds = 60 }),
            NullLogger<OrderStateRecoveryWorker>.Instance);

        await worker.StartAsync(CancellationToken.None);

        var clobClient = new Mock<IPolymarketClobClient>(MockBehavior.Strict);
        var service = NewLiveExecutionService(
            clobClient.Object,
            idempotencyStore,
            orderRepository,
            new RecordingOrderAuditLogger(),
            stateTracker);

        var result = await service.PlaceOrderAsync(request);

        Assert.True(result.Success);
        Assert.Equal("exchange-restored", result.ExchangeOrderId);
        clobClient.Verify(
            client => client.PlaceOrderAsync(
                It.IsAny<OrderRequest>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task OrderStateRecoveryWorker_WhenLiveModeRestoresPendingWithoutExchangeId_AllowsSameRequestRetry()
    {
        var request = NewRequest("recovered-pending");
        var orderRepository = new InMemoryOrderRepository();
        await orderRepository.AddAsync(NewOrderFromRequest(
            request,
            null,
            OrderStatus.Pending,
            orderSalt: "123456789",
            orderTimestamp: "1777374000000"));

        var idempotencyStore = NewIdempotencyStore();
        var stateTracker = new InMemoryOrderStateTracker(NullLogger<InMemoryOrderStateTracker>.Instance);
        using var provider = NewScopedProvider(orderRepository, new RecordingOrderAuditLogger());
        var worker = new OrderStateRecoveryWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            idempotencyStore,
            stateTracker,
            Options.Create(new ExecutionOptions { Mode = ExecutionMode.Live, IdempotencyTtlSeconds = 60 }),
            NullLogger<OrderStateRecoveryWorker>.Instance);

        await worker.StartAsync(CancellationToken.None);

        var clobClient = new Mock<IPolymarketClobClient>(MockBehavior.Strict);
        clobClient
            .Setup(client => client.PlaceOrderAsync(
                It.IsAny<OrderRequest>(),
                request.ClientOrderId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(PolymarketApiResult<OrderResponse>.Success(200, new OrderResponse
            {
                Success = true,
                OrderId = "exchange-retried",
                Status = "LIVE"
            }));

        var service = NewLiveExecutionService(
            clobClient.Object,
            idempotencyStore,
            orderRepository,
            new RecordingOrderAuditLogger(),
            stateTracker);

        var result = await service.PlaceOrderAsync(request);

        Assert.True(result.Success);
        Assert.Equal("exchange-retried", result.ExchangeOrderId);
        Assert.Equal("exchange-retried", (await idempotencyStore.GetAsync(request.ClientOrderId))!.ExchangeOrderId);
    }

    [Fact]
    public async Task HandleOrderEventAsync_WhenEventIsStale_DoesNotRegressStatusOrFilledQuantity()
    {
        var orderRepository = new InMemoryOrderRepository();
        var existing = NewOrder(
            "ws-client",
            "ws-exchange",
            status: OrderStatus.Filled,
            filledQuantity: 10m,
            updatedAtUtc: DateTimeOffset.UtcNow);
        await orderRepository.AddAsync(existing);

        var stateTracker = new Mock<IOrderStateTracker>();
        stateTracker
            .Setup(tracker => tracker.OnOrderStateChangedAsync(It.IsAny<OrderStateUpdate>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        using var provider = NewScopedProvider(orderRepository, new RecordingOrderAuditLogger());
        var worker = new UserOrderEventWorker(
            Mock.Of<IUserOrderEventSource>(),
            provider.GetRequiredService<IServiceScopeFactory>(),
            stateTracker.Object,
            NewIdempotencyStore(),
            NewAccountContext(),
            Options.Create(new ExecutionOptions { Mode = ExecutionMode.Live, EnableUserOrderEvents = true }),
            NullLogger<UserOrderEventWorker>.Instance);

        await InvokePrivateTaskAsync(worker, "HandleOrderEventAsync", new UserOrderEvent
        {
            ExchangeOrderId = "ws-exchange",
            MarketId = "market-1",
            TokenId = "token-1",
            Status = "LIVE",
            Type = "PLACEMENT",
            OriginalSize = "10",
            SizeMatched = "2",
            Price = "0.42",
            TimestampUtc = existing.UpdatedAtUtc.AddMinutes(-1)
        }, CancellationToken.None);

        var updated = await orderRepository.GetByClientOrderIdAsync("ws-client");
        Assert.NotNull(updated);
        Assert.Equal(OrderStatus.Filled, updated.Status);
        Assert.Equal(10m, updated.FilledQuantity);
        Assert.Equal(existing.UpdatedAtUtc, updated.UpdatedAtUtc);
    }

    [Fact]
    public async Task HandleOrderEventAsync_WhenFillIncreases_LogsOnlyFillDelta()
    {
        var orderRepository = new InMemoryOrderRepository();
        var existing = NewOrder(
            "ws-delta-client",
            "ws-delta-exchange",
            status: OrderStatus.PartiallyFilled,
            filledQuantity: 2m,
            updatedAtUtc: DateTimeOffset.UtcNow.AddMinutes(-5));
        await orderRepository.AddAsync(existing);

        var stateTracker = new Mock<IOrderStateTracker>();
        stateTracker
            .Setup(tracker => tracker.OnOrderStateChangedAsync(It.IsAny<OrderStateUpdate>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var auditLogger = new RecordingOrderAuditLogger();
        using var provider = NewScopedProvider(orderRepository, auditLogger);
        var worker = new UserOrderEventWorker(
            Mock.Of<IUserOrderEventSource>(),
            provider.GetRequiredService<IServiceScopeFactory>(),
            stateTracker.Object,
            NewIdempotencyStore(),
            NewAccountContext(),
            Options.Create(new ExecutionOptions { Mode = ExecutionMode.Live, EnableUserOrderEvents = true }),
            NullLogger<UserOrderEventWorker>.Instance);

        await InvokePrivateTaskAsync(worker, "HandleOrderEventAsync", new UserOrderEvent
        {
            ExchangeOrderId = "ws-delta-exchange",
            MarketId = "market-1",
            TokenId = "token-1",
            Status = "LIVE",
            Type = "UPDATE",
            OriginalSize = "10",
            SizeMatched = "4",
            Price = "0.42",
            TimestampUtc = existing.UpdatedAtUtc.AddMinutes(1)
        }, CancellationToken.None);

        var fill = Assert.Single(auditLogger.Fills);
        Assert.Equal("ws-delta-client", fill.ClientOrderId);
        Assert.Equal(2m, fill.FilledQuantity);
    }

    [Fact]
    public async Task HandleTradeEventAsync_WhenOrderEventAlreadyAdvancedFill_DoesNotDoubleCountAndLogsTrade()
    {
        var orderRepository = new InMemoryOrderRepository();
        var tradeRepository = new InMemoryTradeRepository();
        var auditLogger = new RecordingOrderAuditLogger();
        var existing = NewOrder(
            "ws-client",
            "ws-exchange",
            status: OrderStatus.Open,
            filledQuantity: 0m,
            updatedAtUtc: DateTimeOffset.UtcNow.AddMinutes(-5));
        await orderRepository.AddAsync(existing);

        var stateTracker = new Mock<IOrderStateTracker>();
        stateTracker
            .Setup(tracker => tracker.OnOrderStateChangedAsync(It.IsAny<OrderStateUpdate>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        using var provider = NewScopedProvider(orderRepository, auditLogger, tradeRepository);
        var worker = new UserOrderEventWorker(
            Mock.Of<IUserOrderEventSource>(),
            provider.GetRequiredService<IServiceScopeFactory>(),
            stateTracker.Object,
            NewIdempotencyStore(),
            NewAccountContext(),
            Options.Create(new ExecutionOptions { Mode = ExecutionMode.Live, EnableUserOrderEvents = true }),
            NullLogger<UserOrderEventWorker>.Instance);

        await InvokePrivateTaskAsync(worker, "HandleOrderEventAsync", new UserOrderEvent
        {
            ExchangeOrderId = "ws-exchange",
            MarketId = "market-1",
            TokenId = "token-1",
            Status = "LIVE",
            Type = "PLACEMENT",
            OriginalSize = "10",
            SizeMatched = "2",
            Price = "0.42",
            TimestampUtc = existing.UpdatedAtUtc.AddMinutes(1)
        }, CancellationToken.None);

        var afterOrderEvent = await orderRepository.GetByClientOrderIdAsync("ws-client");
        Assert.NotNull(afterOrderEvent);
        Assert.Equal(2m, afterOrderEvent.FilledQuantity);

        await InvokePrivateTaskAsync(worker, "HandleTradeEventAsync", new UserTradeEvent
        {
            ExchangeTradeId = "trade-1",
            ExchangeOrderId = "ws-exchange",
            MarketId = "market-1",
            TokenId = "token-1",
            Side = "BUY",
            Outcome = "Yes",
            Status = "CONFIRMED",
            Price = "0.42",
            Size = "2",
            FeeRateBps = "0",
            TimestampUtc = existing.UpdatedAtUtc.AddMinutes(2)
        }, CancellationToken.None);

        var afterTradeEvent = await orderRepository.GetByClientOrderIdAsync("ws-client");
        Assert.NotNull(afterTradeEvent);
        Assert.Equal(OrderStatus.PartiallyFilled, afterTradeEvent.Status);
        Assert.Equal(2m, afterTradeEvent.FilledQuantity);

        var trade = Assert.Single(auditLogger.Trades);
        Assert.Equal("ws-client", trade.ClientOrderId);
        Assert.Equal("trade-1", trade.ExchangeTradeId);
        Assert.Equal(2m, trade.Quantity);
        Assert.Equal(0.42m, trade.Price);
    }

    [Fact]
    public async Task HandleTradeEventAsync_WhenLocalOrderIsMakerAndMakerSideMissing_LogsPersistedOrderSide()
    {
        var orderRepository = new InMemoryOrderRepository();
        var tradeRepository = new InMemoryTradeRepository();
        var auditLogger = new RecordingOrderAuditLogger();
        var existing = NewOrder(
            "ws-maker-client",
            "ws-maker-exchange",
            status: OrderStatus.Open,
            filledQuantity: 0m,
            updatedAtUtc: DateTimeOffset.UtcNow.AddMinutes(-5)) with
        {
            Side = OrderSide.Sell
        };
        await orderRepository.AddAsync(existing);

        var stateTracker = new Mock<IOrderStateTracker>();
        stateTracker
            .Setup(tracker => tracker.OnOrderStateChangedAsync(It.IsAny<OrderStateUpdate>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        using var provider = NewScopedProvider(orderRepository, auditLogger, tradeRepository);
        var worker = new UserOrderEventWorker(
            Mock.Of<IUserOrderEventSource>(),
            provider.GetRequiredService<IServiceScopeFactory>(),
            stateTracker.Object,
            NewIdempotencyStore(),
            NewAccountContext(),
            Options.Create(new ExecutionOptions { Mode = ExecutionMode.Live, EnableUserOrderEvents = true }),
            NullLogger<UserOrderEventWorker>.Instance);

        await InvokePrivateTaskAsync(worker, "HandleTradeEventAsync", new UserTradeEvent
        {
            ExchangeTradeId = "ws-maker-trade",
            ExchangeOrderId = "other-taker",
            MarketId = "market-1",
            TokenId = "token-1",
            Side = "BUY",
            Outcome = "Yes",
            Status = "CONFIRMED",
            Price = "0.42",
            Size = "2",
            FeeRateBps = "0",
            MakerOrders = new[]
            {
                new UserTradeMakerOrder
                {
                    ExchangeOrderId = "ws-maker-exchange",
                    AssetId = "token-1",
                    Outcome = "Yes",
                    MatchedAmount = "2"
                }
            },
            TimestampUtc = existing.UpdatedAtUtc.AddMinutes(2)
        }, CancellationToken.None);

        var trade = Assert.Single(auditLogger.Trades);
        Assert.Equal("ws-maker-client", trade.ClientOrderId);
        Assert.Equal("ws-maker-trade", trade.ExchangeTradeId);
        Assert.Equal(OrderSide.Sell, trade.Side);
    }

    [Fact]
    public async Task HandleTradeEventAsync_WhenMatchedThenFailed_DoesNotWriteTradeOrAdvanceFill()
    {
        var orderRepository = new InMemoryOrderRepository();
        var tradeRepository = new InMemoryTradeRepository();
        var auditLogger = new RecordingOrderAuditLogger();
        var existing = NewOrder(
            "ws-failed-client",
            "ws-failed-exchange",
            status: OrderStatus.Open,
            filledQuantity: 0m,
            updatedAtUtc: DateTimeOffset.UtcNow.AddMinutes(-5));
        await orderRepository.AddAsync(existing);

        var stateTracker = new Mock<IOrderStateTracker>();
        stateTracker
            .Setup(tracker => tracker.OnOrderStateChangedAsync(It.IsAny<OrderStateUpdate>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        using var provider = NewScopedProvider(orderRepository, auditLogger, tradeRepository);
        var worker = new UserOrderEventWorker(
            Mock.Of<IUserOrderEventSource>(),
            provider.GetRequiredService<IServiceScopeFactory>(),
            stateTracker.Object,
            NewIdempotencyStore(),
            NewAccountContext(),
            Options.Create(new ExecutionOptions { Mode = ExecutionMode.Live, EnableUserOrderEvents = true }),
            NullLogger<UserOrderEventWorker>.Instance);

        var matched = new UserTradeEvent
        {
            ExchangeTradeId = "trade-failed",
            ExchangeOrderId = "ws-failed-exchange",
            MarketId = "market-1",
            TokenId = "token-1",
            Side = "BUY",
            Outcome = "Yes",
            Status = "MATCHED",
            Price = "0.42",
            Size = "2",
            FeeRateBps = "0",
            TimestampUtc = existing.UpdatedAtUtc.AddMinutes(1)
        };

        await InvokePrivateTaskAsync(worker, "HandleTradeEventAsync", matched, CancellationToken.None);
        await InvokePrivateTaskAsync(worker, "HandleTradeEventAsync", matched with { Status = "FAILED" }, CancellationToken.None);

        var updated = await orderRepository.GetByClientOrderIdAsync("ws-failed-client");
        Assert.NotNull(updated);
        Assert.Equal(OrderStatus.Open, updated.Status);
        Assert.Equal(0m, updated.FilledQuantity);
        Assert.Empty(auditLogger.Trades);
        Assert.Empty(await tradeRepository.GetByClientOrderIdAsync("ws-failed-client"));
    }

    [Fact]
    public async Task PaperPlaceOrderAsync_WhenGeoKycUnconfirmed_WarnsWithoutOrderErrorAndDoesNotRepeatForDuplicate()
    {
        var request = NewRequest("paper-compliance-warning");
        var idempotencyStore = NewIdempotencyStore();
        var orderRepository = new InMemoryOrderRepository();
        var stateTracker = new InMemoryOrderStateTracker(NullLogger<InMemoryOrderStateTracker>.Instance);
        var riskEvents = new InMemoryRiskEventRepository(NullLogger<InMemoryRiskEventRepository>.Instance);
        var service = NewPaperExecutionService(idempotencyStore, orderRepository, stateTracker, riskEvents);

        var first = await service.PlaceOrderAsync(request);
        var second = await service.PlaceOrderAsync(request);

        Assert.True(first.Success);
        Assert.True(second.Success);
        Assert.Equal(first.ExchangeOrderId, second.ExchangeOrderId);
        var warnings = await riskEvents.QueryAsync(request.StrategyId);
        var warning = Assert.Single(warnings);
        Assert.Equal("COMPLIANCE_GEO_KYC_UNCONFIRMED", warning.Code);
        Assert.Equal(RiskSeverity.Warning, warning.Severity);
    }

    [Fact]
    public async Task ClobUserOrderEventSource_WhenConnected_AddsMarketsWithDynamicSubscribeMessage()
    {
        var connection = new FakeClobUserWebSocketConnection();
        var source = NewClobUserOrderEventSource(connection);

        await source.SubscribeMarketsAsync(new[] { "market-1" });
        await source.ConnectAsync();
        await source.SubscribeMarketsAsync(new[] { "market-2" });

        var dynamicMessage = connection.SentMessages.Last(message => message.Contains("\"operation\":\"subscribe\"", StringComparison.Ordinal));
        using var document = JsonDocument.Parse(dynamicMessage);
        var root = document.RootElement;
        Assert.Equal("subscribe", root.GetProperty("operation").GetString());
        Assert.Equal("market-2", root.GetProperty("markets")[0].GetString());
        Assert.False(root.TryGetProperty("auth", out _));

        await source.DisposeAsync();
    }

    [Fact]
    public async Task ClobUserOrderEventSource_SendsUppercasePingHeartbeat()
    {
        var connection = new FakeClobUserWebSocketConnection();
        var source = NewClobUserOrderEventSource(connection, heartbeatIntervalMs: 1);

        await source.SubscribeMarketsAsync(new[] { "market-1" });
        await source.ConnectAsync();

        var deadline = DateTimeOffset.UtcNow.AddSeconds(2);
        while (!connection.SentMessages.Contains("PING") && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        Assert.Contains("PING", connection.SentMessages);
        await source.DisposeAsync();
    }

    [Fact]
    public async Task ClobUserOrderEventSource_ParsesTradeMatchtimeFallback()
    {
        var connection = new FakeClobUserWebSocketConnection();
        var source = NewClobUserOrderEventSource(connection);
        var received = new List<UserTradeEvent>();
        using var _ = source.OnTrade((trade, _) =>
        {
            received.Add(trade);
            return Task.CompletedTask;
        });

        await InvokePrivateTaskAsync(
            source,
            "ProcessMessageAsync",
            """
            {
              "event_type": "trade",
              "id": "trade-matchtime",
              "status": "CONFIRMED",
              "matchtime": "1777374000000",
              "price": "0.42",
              "size": "2"
            }
            """,
            CancellationToken.None);

        var trade = Assert.Single(received);
        Assert.Equal("trade-matchtime", trade.ExchangeTradeId);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1_777_374_000_000L), trade.TimestampUtc);
        await source.DisposeAsync();
    }

    private static LiveExecutionService NewLiveExecutionService(
        IPolymarketClobClient clobClient,
        IIdempotencyStore idempotencyStore,
        IOrderRepository orderRepository,
        IOrderAuditLogger auditLogger,
        IOrderStateTracker? stateTrackerOverride = null,
        ExecutionOptions? optionsOverride = null,
        IComplianceGuard? complianceGuard = null,
        IRiskEventRepository? riskEventRepository = null,
        IRiskManager? riskManager = null,
        ILiveArmingService? liveArmingService = null)
    {
        var stateTracker = stateTrackerOverride;
        if (stateTracker is null)
        {
            var stateTrackerMock = new Mock<IOrderStateTracker>();
            stateTrackerMock.Setup(tracker => tracker.GetOpenOrderCount(It.IsAny<string>())).Returns(0);
            stateTrackerMock.Setup(tracker => tracker.GetAllOpenOrderCounts()).Returns(new Dictionary<string, int>());
            stateTrackerMock.Setup(tracker => tracker.GetOpenOrders()).Returns(Array.Empty<OrderStateUpdate>());
            stateTrackerMock
                .Setup(tracker => tracker.OnOrderStateChangedAsync(It.IsAny<OrderStateUpdate>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            stateTracker = stateTrackerMock.Object;
        }

        var options = optionsOverride ?? new ExecutionOptions
        {
            Mode = ExecutionMode.Live,
            UseBatchOrders = true,
            MaxBatchOrderSize = 15,
            MaxOpenOrdersPerMarket = 10,
            IdempotencyTtlSeconds = 60
        };

        var constructor = typeof(LiveExecutionService).GetConstructors().Single();
        var arguments = constructor.GetParameters()
            .Select(parameter => CreateLiveExecutionServiceArgument(
                parameter.ParameterType,
                clobClient,
                idempotencyStore,
                stateTracker,
                new OrderLimitValidator(stateTracker, Options.Create(options)),
                auditLogger,
                riskManager ?? CreateRiskManagerStub(),
                riskEventRepository ?? new InMemoryRiskEventRepository(NullLogger<InMemoryRiskEventRepository>.Instance),
                orderRepository,
                NewAccountContext(),
                Options.Create(options),
                complianceGuard,
                liveArmingService ?? AlwaysArmedLiveArmingService.Instance))
            .ToArray();

        return (LiveExecutionService)constructor.Invoke(arguments);
    }

    private static PaperExecutionService NewPaperExecutionService(
        IIdempotencyStore idempotencyStore,
        IOrderRepository orderRepository,
        IOrderStateTracker stateTracker,
        IRiskEventRepository riskEventRepository)
    {
        var executionOptions = new ExecutionOptions
        {
            Mode = ExecutionMode.Paper,
            MaxOpenOrdersPerMarket = 10,
            IdempotencyTtlSeconds = 60
        };

        var complianceGuard = new ComplianceGuard(
            Options.Create(new ComplianceOptions { GeoKycAllowed = false }),
            Options.Create(executionOptions),
            Options.Create(new RiskOptions()),
            Options.Create(new ComplianceStrategyEngineOptions()));

        var orderBookReader = new Mock<IOrderBookReader>();
        orderBookReader.Setup(reader => reader.GetTopOfBook(It.IsAny<string>())).Returns((TopOfBookDto?)null);
        orderBookReader.Setup(reader => reader.GetDepth(It.IsAny<string>(), It.IsAny<int>()))
            .Returns(Array.Empty<PriceLevelDto>());

        return new PaperExecutionService(
            orderBookReader.Object,
            idempotencyStore,
            stateTracker,
            new OrderLimitValidator(stateTracker, Options.Create(executionOptions)),
            complianceGuard,
            new RecordingOrderAuditLogger(),
            riskEventRepository,
            orderRepository,
            new PaperOrderStore(),
            NewAccountContext(),
            Options.Create(new PaperTradingOptions
            {
                DefaultFillRate = 0d,
                DeterministicSeed = 1
            }),
            Options.Create(executionOptions),
            NullLogger<PaperExecutionService>.Instance);
    }

    private static ClobUserOrderEventSource NewClobUserOrderEventSource(
        FakeClobUserWebSocketConnection connection,
        int heartbeatIntervalMs = 10_000)
    {
        return new ClobUserOrderEventSource(
            Options.Create(new UserOrderEventSourceOptions
            {
                ClobUserUrl = "wss://example.test/ws/user",
                ClobHeartbeatIntervalMs = heartbeatIntervalMs,
                ConnectionTimeoutMs = 1000
            }),
            Options.Create(new PolymarketClobOptions
            {
                ApiKey = "api-key",
                ApiSecret = "api-secret",
                ApiPassphrase = "api-passphrase"
            }),
            new FakeClobUserWebSocketConnectionFactory(connection),
            NullLogger<ClobUserOrderEventSource>.Instance);
    }

    private static ServiceProvider NewScopedProvider(
        IOrderRepository orderRepository,
        IOrderAuditLogger auditLogger,
        ITradeRepository? tradeRepository = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(orderRepository);
        services.AddSingleton(auditLogger);
        services.AddSingleton(tradeRepository ?? new InMemoryTradeRepository());
        services.AddSingleton<IRiskManager>(_ =>
        {
            var riskManager = new Mock<IRiskManager>();
            riskManager
                .Setup(manager => manager.RecordOrderUpdateAsync(It.IsAny<RiskOrderUpdate>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            riskManager
                .Setup(manager => manager.RecordOrderErrorAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            return riskManager.Object;
        });
        services.AddSingleton(NewAccountContext());
        return services.BuildServiceProvider();
    }

    private static InMemoryIdempotencyStore NewIdempotencyStore() =>
        new(NullLogger<InMemoryIdempotencyStore>.Instance);

    private static IRiskManager CreateRiskManagerStub()
    {
        var riskManager = new Mock<IRiskManager>();
        riskManager
            .Setup(manager => manager.RecordOrderErrorAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return riskManager.Object;
    }

    private static TradingAccountContext NewAccountContext()
    {
        var context = new TradingAccountContext();
        context.Initialize(AccountId, "test-account");
        return context;
    }

    private static ExecutionRequest NewRequest(string clientOrderId) => new()
    {
        ClientOrderId = clientOrderId,
        StrategyId = "strategy-1",
        CorrelationId = "correlation-1",
        MarketId = "market-1",
        TokenId = "token-1",
        Outcome = OutcomeSide.Yes,
        Side = OrderSide.Buy,
        OrderType = OrderType.Limit,
        TimeInForce = TimeInForce.Gtc,
        Price = 0.42m,
        Quantity = 10m
    };

    private static OrderDto NewOrder(
        string clientOrderId,
        string exchangeOrderId,
        OrderStatus status,
        decimal filledQuantity = 0m,
        DateTimeOffset? updatedAtUtc = null) => new(
        Guid.NewGuid(),
        AccountId,
        "old-market",
        "old-token",
        "strategy-1",
        clientOrderId,
        exchangeOrderId,
        "correlation-1",
        OutcomeSide.Yes,
        OrderSide.Buy,
        OrderType.Limit,
        TimeInForce.Gtc,
        null,
        false,
        0.42m,
        10m,
        filledQuantity,
        status,
        null,
        DateTimeOffset.UtcNow.AddMinutes(-10),
        updatedAtUtc ?? DateTimeOffset.UtcNow.AddMinutes(-5));

    private static OrderDto NewOrderFromRequest(
        ExecutionRequest request,
        string? exchangeOrderId,
        OrderStatus status,
        string? orderSalt = null,
        string? orderTimestamp = null) => new(
        Guid.NewGuid(),
        AccountId,
        request.MarketId,
        request.TokenId,
        request.StrategyId,
        request.ClientOrderId,
        exchangeOrderId,
        request.CorrelationId,
        request.Outcome,
        request.Side,
        request.OrderType,
        request.TimeInForce,
        request.GoodTilDateUtc,
        request.NegRisk,
        request.Price,
        request.Quantity,
        0m,
        status,
        null,
        DateTimeOffset.UtcNow.AddMinutes(-10),
        DateTimeOffset.UtcNow.AddMinutes(-5),
        orderSalt,
        orderTimestamp);

    private static async Task InvokePrivateTaskAsync(object target, string methodName, params object?[] args)
    {
        var method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(target.GetType().FullName, methodName);
        var task = (Task?)method.Invoke(target, args)
            ?? throw new InvalidOperationException($"Method {methodName} did not return a Task.");
        await task.ConfigureAwait(false);
    }

    private static object CreateLiveExecutionServiceArgument(
        Type parameterType,
        IPolymarketClobClient clobClient,
        IIdempotencyStore idempotencyStore,
        IOrderStateTracker stateTracker,
        OrderLimitValidator limitValidator,
        IOrderAuditLogger auditLogger,
        IRiskManager riskManager,
        IRiskEventRepository riskEventRepository,
        IOrderRepository orderRepository,
        TradingAccountContext accountContext,
        IOptions<ExecutionOptions> options,
        IComplianceGuard? complianceGuard,
        ILiveArmingService liveArmingService)
    {
        if (parameterType == typeof(IPolymarketClobClient)) return clobClient;
        if (parameterType == typeof(IPolymarketOrderSigner)) return new DeterministicOrderSigner();
        if (parameterType == typeof(IIdempotencyStore)) return idempotencyStore;
        if (parameterType == typeof(IOrderStateTracker)) return stateTracker;
        if (parameterType == typeof(OrderLimitValidator)) return limitValidator;
        if (parameterType == typeof(IOrderAuditLogger)) return auditLogger;
        if (parameterType == typeof(IRiskManager)) return riskManager;
        if (parameterType == typeof(IRiskEventRepository)) return riskEventRepository;
        if (parameterType == typeof(IOrderRepository)) return orderRepository;
        if (parameterType == typeof(TradingAccountContext)) return accountContext;
        if (parameterType == typeof(IOptions<ExecutionOptions>)) return options;
        if (parameterType == typeof(ILiveArmingService)) return liveArmingService;
        if (parameterType == typeof(ILogger<LiveExecutionService>)) return NullLogger<LiveExecutionService>.Instance;
        if (parameterType == typeof(IComplianceGuard))
        {
            return complianceGuard ?? CreateAllowProxy(parameterType);
        }

        throw new InvalidOperationException($"Unsupported LiveExecutionService constructor argument: {parameterType.FullName}");
    }

    private sealed class DeterministicOrderSigner : IPolymarketOrderSigner
    {
        public PostOrderRequest CreatePostOrderRequest(OrderRequest request, string? idempotencyKey = null)
        {
            return new PostOrderRequest
            {
                Owner = "test-api-key",
                OrderType = request.TimeInForce ?? "GTC",
                DeferExecution = false,
                Order = new SignedClobOrder
                {
                    Salt = request.Salt ?? "1",
                    Maker = "0x1234567890abcdef1234567890abcdef12345678",
                    Signer = "0x1234567890abcdef1234567890abcdef12345678",
                    TokenId = request.TokenId,
                    MakerAmount = "4200000",
                    TakerAmount = "10000000",
                    Expiration = request.Expiration?.ToString() ?? "0",
                    Side = request.Side,
                    SignatureType = 0,
                    Timestamp = request.Timestamp ?? "1777374000000",
                    Metadata = "0x0000000000000000000000000000000000000000000000000000000000000000",
                    Builder = "0x0000000000000000000000000000000000000000000000000000000000000000",
                    Signature = "0xsignature"
                }
            };
        }
    }

    private static object CreateAllowProxy(Type interfaceType)
    {
        var method = typeof(DispatchProxy)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(method => method.Name == nameof(DispatchProxy.Create) && method.GetGenericArguments().Length == 2);
        return method.MakeGenericMethod(interfaceType, typeof(AllowProxy)).Invoke(null, null)!;
    }

    private class AllowProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            var returnType = targetMethod?.ReturnType;
            var disabledProperty = returnType?.GetProperty("Disabled", BindingFlags.Public | BindingFlags.Static);
            return disabledProperty?.GetValue(null);
        }
    }

    private sealed class BlockingComplianceGuard : IComplianceGuard
    {
        private static readonly ComplianceCheckResult Blocked = new(
            Enabled: true,
            IsCompliant: false,
            BlocksOrders: true,
            Issues: new[]
            {
                new ComplianceIssue(
                    "COMPLIANCE_TEST_BLOCK",
                    ComplianceSeverity.Error,
                    "blocked by test",
                    BlocksLiveOrders: true)
            });

        public ComplianceCheckResult CheckConfiguration(ExecutionMode executionMode) => Blocked;

        public ComplianceCheckResult CheckOrderPlacement(ExecutionMode executionMode) => Blocked;
    }

    private sealed class WarningComplianceGuard : IComplianceGuard
    {
        private static readonly ComplianceCheckResult Warning = new(
            Enabled: true,
            IsCompliant: true,
            BlocksOrders: false,
            Issues: new[]
            {
                new ComplianceIssue(
                    "COMPLIANCE_TEST_WARNING",
                    ComplianceSeverity.Warning,
                    "allowed by test override",
                    BlocksLiveOrders: false)
            });

        public ComplianceCheckResult CheckConfiguration(ExecutionMode executionMode) => Warning;

        public ComplianceCheckResult CheckOrderPlacement(ExecutionMode executionMode) => Warning;
    }

    private sealed class AlwaysArmedLiveArmingService : ILiveArmingService
    {
        public static readonly AlwaysArmedLiveArmingService Instance = new();

        private static readonly LiveArmingStatus Armed = new(
            true,
            "Armed",
            "Live trading is armed for regression test.",
            "test",
            new DateTimeOffset(2026, 5, 3, 8, 30, 0, TimeSpan.Zero),
            new LiveArmingEvidence(
                "test-evidence",
                "test",
                "regression",
                new DateTimeOffset(2026, 5, 3, 8, 25, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 5, 3, 12, 30, 0, TimeSpan.Zero),
                "test",
                "fingerprint",
                new LiveArmingRiskSummary(100m, 100m, 0m, 0m, 0, 0, false),
                []),
            []);

        public Task<LiveArmingStatus> GetStatusAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(Armed);

        public Task<LiveArmingResult> ArmAsync(
            LiveArmingRequest request,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new LiveArmingResult(true, "Accepted", "Already armed.", Armed));

        public Task<LiveArmingResult> DisarmAsync(
            LiveDisarmingRequest request,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new LiveArmingResult(true, "Accepted", "Disarmed.", Armed));

        public Task<LiveArmingStatus> RequireArmedAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(Armed);
    }

    private sealed class BlockedLiveArmingService : ILiveArmingService
    {
        private static readonly LiveArmingStatus Blocked = new(
            false,
            "NotArmed",
            "Live arming evidence has not been recorded.",
            "test",
            new DateTimeOffset(2026, 5, 3, 8, 30, 0, TimeSpan.Zero),
            null,
            ["Live arming evidence has not been recorded."]);

        public Task<LiveArmingStatus> GetStatusAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(Blocked);

        public Task<LiveArmingResult> ArmAsync(
            LiveArmingRequest request,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new LiveArmingResult(false, "Blocked", "Blocked.", Blocked));

        public Task<LiveArmingResult> DisarmAsync(
            LiveDisarmingRequest request,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new LiveArmingResult(true, "Accepted", "Disarmed.", Blocked));

        public Task<LiveArmingStatus> RequireArmedAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(Blocked);
    }

    private sealed class FakeClobUserWebSocketConnectionFactory(
        FakeClobUserWebSocketConnection connection) : IClobUserWebSocketConnectionFactory
    {
        public IClobUserWebSocketConnection Create() => connection;
    }

    private sealed class FakeClobUserWebSocketConnection : IClobUserWebSocketConnection
    {
        public List<string> SentMessages { get; } = new();

        public WebSocketState State { get; private set; } = WebSocketState.None;

        public Task ConnectAsync(Uri uri, CancellationToken cancellationToken)
        {
            State = WebSocketState.Open;
            return Task.CompletedTask;
        }

        public Task SendTextAsync(string message, CancellationToken cancellationToken)
        {
            SentMessages.Add(message);
            return Task.CompletedTask;
        }

        public async Task<WebSocketReceiveResult> ReceiveAsync(
            ArraySegment<byte> buffer,
            CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            return new WebSocketReceiveResult(0, WebSocketMessageType.Close, true);
        }

        public Task CloseAsync(
            WebSocketCloseStatus closeStatus,
            string? statusDescription,
            CancellationToken cancellationToken)
        {
            State = WebSocketState.Closed;
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            State = WebSocketState.Closed;
        }
    }

    private sealed class RecordingOrderAuditLogger : IOrderAuditLogger
    {
        public List<RecordedSubmission> Submissions { get; } = new();
        public List<(string ClientOrderId, string ErrorCode, string ErrorMessage)> Rejections { get; } = new();
        public List<RecordedFill> Fills { get; } = new();
        public List<RecordedTrade> Trades { get; } = new();

        public Task LogOrderCreatedAsync(
            Guid orderId,
            string clientOrderId,
            string strategyId,
            string marketId,
            string? correlationId,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task LogOrderSubmittedAsync(
            Guid orderId,
            string clientOrderId,
            string strategyId,
            string marketId,
            string? correlationId,
            CancellationToken cancellationToken = default)
        {
            Submissions.Add(new RecordedSubmission(clientOrderId));
            return Task.CompletedTask;
        }

        public Task LogOrderAcceptedAsync(
            Guid orderId,
            string clientOrderId,
            string strategyId,
            string marketId,
            string? exchangeOrderId,
            string? correlationId,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task LogOrderRejectedAsync(
            Guid orderId,
            string clientOrderId,
            string strategyId,
            string marketId,
            string errorCode,
            string errorMessage,
            string? correlationId,
            CancellationToken cancellationToken = default)
        {
            Rejections.Add((clientOrderId, errorCode, errorMessage));
            return Task.CompletedTask;
        }

        public Task LogOrderFilledAsync(
            Guid orderId,
            string clientOrderId,
            string strategyId,
            string marketId,
            decimal filledQuantity,
            decimal fillPrice,
            bool isPartial,
            string? correlationId,
            CancellationToken cancellationToken = default)
        {
            Fills.Add(new RecordedFill(clientOrderId, filledQuantity, fillPrice, isPartial));
            return Task.CompletedTask;
        }

        public Task LogOrderCancelledAsync(
            Guid orderId,
            string clientOrderId,
            string strategyId,
            string marketId,
            string? correlationId,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task LogOrderExpiredAsync(
            Guid orderId,
            string clientOrderId,
            string strategyId,
            string marketId,
            string? correlationId,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task LogTradeAsync(
            Guid orderId,
            Guid tradingAccountId,
            string clientOrderId,
            string strategyId,
            string marketId,
            string tokenId,
            OutcomeSide outcome,
            OrderSide side,
            decimal price,
            decimal quantity,
            string exchangeTradeId,
            decimal fee,
            string? correlationId,
            CancellationToken cancellationToken = default)
        {
            Trades.Add(new RecordedTrade(clientOrderId, exchangeTradeId, side, quantity, price));
            return Task.CompletedTask;
        }
    }

    private sealed record RecordedTrade(
        string ClientOrderId,
        string ExchangeTradeId,
        OrderSide Side,
        decimal Quantity,
        decimal Price);

    private sealed record RecordedSubmission(string ClientOrderId);

    private sealed record RecordedFill(
        string ClientOrderId,
        decimal FilledQuantity,
        decimal FillPrice,
        bool IsPartial);

    private sealed class InMemoryOrderRepository : IOrderRepository
    {
        private readonly Dictionary<Guid, OrderDto> _orders = new();

        public Task AddAsync(OrderDto order, CancellationToken cancellationToken = default)
        {
            _orders[order.Id] = order;
            return Task.CompletedTask;
        }

        public Task AddRangeAsync(IEnumerable<OrderDto> orders, CancellationToken cancellationToken = default)
        {
            foreach (var order in orders)
            {
                _orders[order.Id] = order;
            }

            return Task.CompletedTask;
        }

        public Task UpdateAsync(OrderDto order, CancellationToken cancellationToken = default)
        {
            _orders[order.Id] = order;
            return Task.CompletedTask;
        }

        public Task<OrderDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(_orders.GetValueOrDefault(id));

        public Task<OrderDto?> GetByClientOrderIdAsync(string clientOrderId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_orders.Values.FirstOrDefault(order => order.ClientOrderId == clientOrderId));

        public Task<OrderDto?> GetByExchangeOrderIdAsync(string exchangeOrderId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_orders.Values.SingleOrDefault(order => order.ExchangeOrderId == exchangeOrderId));

        public Task<IReadOnlyList<OrderDto>> GetOpenOrdersAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<OrderDto>>(_orders.Values
                .Where(order => order.Status is OrderStatus.Pending or OrderStatus.Open or OrderStatus.PartiallyFilled)
                .ToArray());

        public Task<IReadOnlyList<OrderDto>> GetByStrategyIdAsync(
            string strategyId,
            DateTimeOffset? from = null,
            DateTimeOffset? to = null,
            int? limit = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<OrderDto>>(_orders.Values
                .Where(order => order.StrategyId == strategyId)
                .Take(limit ?? int.MaxValue)
                .ToArray());

        public Task<IReadOnlyList<OrderDto>> GetByMarketIdAsync(
            string marketId,
            DateTimeOffset? from = null,
            DateTimeOffset? to = null,
            int? limit = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<OrderDto>>(_orders.Values
                .Where(order => order.MarketId == marketId)
                .Take(limit ?? int.MaxValue)
                .ToArray());

        public Task<IReadOnlyList<OrderDto>> GetByStatusAsync(
            OrderStatus status,
            DateTimeOffset? from = null,
            DateTimeOffset? to = null,
            int? limit = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<OrderDto>>(_orders.Values
                .Where(order => order.Status == status)
                .Take(limit ?? int.MaxValue)
                .ToArray());

        public Task<PagedResultDto<OrderDto>> GetPagedAsync(
            int page,
            int pageSize,
            string? strategyId = null,
            string? marketId = null,
            OrderStatus? status = null,
            DateTimeOffset? from = null,
            DateTimeOffset? to = null,
            CancellationToken cancellationToken = default)
        {
            var items = _orders.Values.ToArray();
            return Task.FromResult(new PagedResultDto<OrderDto>(items, items.Length, page, pageSize));
        }

        public Task<int> DeleteBeforeAsync(DateTimeOffset beforeUtc, CancellationToken cancellationToken = default) =>
            Task.FromResult(0);
    }

    private sealed class InMemoryTradeRepository : ITradeRepository
    {
        private readonly Dictionary<Guid, TradeDto> _trades = new();

        public Task<TradeDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(_trades.GetValueOrDefault(id));

        public Task<IReadOnlyList<TradeDto>> GetByOrderIdAsync(Guid orderId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TradeDto>>(_trades.Values
                .Where(trade => trade.OrderId == orderId)
                .ToArray());

        public Task<IReadOnlyList<TradeDto>> GetByClientOrderIdAsync(
            string clientOrderId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TradeDto>>(_trades.Values
                .Where(trade => trade.ClientOrderId == clientOrderId)
                .ToArray());

        public Task<TradeDto?> GetByExchangeTradeIdAsync(
            string exchangeTradeId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_trades.Values.FirstOrDefault(trade => trade.ExchangeTradeId == exchangeTradeId));

        public Task<IReadOnlyList<TradeDto>> GetByStrategyIdAsync(
            string strategyId,
            DateTimeOffset? from = null,
            DateTimeOffset? to = null,
            int? limit = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TradeDto>>(_trades.Values
                .Where(trade => trade.StrategyId == strategyId)
                .Take(limit ?? int.MaxValue)
                .ToArray());

        public Task<IReadOnlyList<TradeDto>> GetByMarketIdAsync(
            string marketId,
            DateTimeOffset? from = null,
            DateTimeOffset? to = null,
            int? limit = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TradeDto>>(_trades.Values
                .Where(trade => trade.MarketId == marketId)
                .Take(limit ?? int.MaxValue)
                .ToArray());

        public Task<PagedResultDto<TradeDto>> GetPagedAsync(
            int page,
            int pageSize,
            string? strategyId = null,
            string? marketId = null,
            DateTimeOffset? from = null,
            DateTimeOffset? to = null,
            CancellationToken cancellationToken = default)
        {
            var items = _trades.Values.ToArray();
            return Task.FromResult(new PagedResultDto<TradeDto>(items, items.Length, page, pageSize));
        }

        public Task AddAsync(TradeDto trade, CancellationToken cancellationToken = default)
        {
            _trades[trade.Id] = trade;
            return Task.CompletedTask;
        }

        public Task AddRangeAsync(IEnumerable<TradeDto> trades, CancellationToken cancellationToken = default)
        {
            foreach (var trade in trades)
            {
                _trades[trade.Id] = trade;
            }

            return Task.CompletedTask;
        }

        public Task<int> DeleteBeforeAsync(DateTimeOffset beforeUtc, CancellationToken cancellationToken = default) =>
            Task.FromResult(0);

        public Task<PnLSummary> GetPnLSummaryAsync(
            string strategyId,
            DateTimeOffset? from = null,
            DateTimeOffset? to = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new PnLSummary(strategyId, 0m, 0m, 0m, 0, from, to));
    }
}
