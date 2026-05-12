using Autotrade.Api.Controllers;
using Autotrade.ArcSettlement.Application.Contract.Revenue;
using Microsoft.AspNetCore.Mvc;

namespace Autotrade.Api.Tests;

public sealed class ArcRevenueControllerContractTests
{
    [Fact]
    public async Task ListAsync_ReturnsRevenueSettlementRecords()
    {
        var service = new FakeArcRevenueSettlementRecorder
        {
            Records = [CreateRecord()]
        };
        var controller = new ArcRevenueController(service);

        var result = await controller.ListAsync(limit: 5, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var records = Assert.IsAssignableFrom<IReadOnlyList<ArcRevenueSettlementRecord>>(ok.Value);
        Assert.Single(records);
        Assert.Equal(5, service.LastListLimit);
    }

    [Fact]
    public async Task GetAsync_ReturnsNotFoundForMissingSettlement()
    {
        var controller = new ArcRevenueController(new FakeArcRevenueSettlementRecorder());

        var result = await controller.GetAsync(SettlementId, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task RecordAsync_ForwardsRequestAndReturnsResult()
    {
        var expected = CreateRecord();
        var service = new FakeArcRevenueSettlementRecorder
        {
            RecordResult = new ArcRevenueSettlementResult(expected, AlreadyRecorded: false)
        };
        var controller = new ArcRevenueController(service);
        var request = CreateRequest();

        var result = await controller.RecordAsync(request, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<ArcRevenueSettlementResult>(ok.Value);
        Assert.Equal(SettlementId, response.Record.SettlementId);
        Assert.Same(request, service.LastRecordRequest);
    }

    [Fact]
    public async Task RecordAsync_MapsValidationFailureToBadRequest()
    {
        var service = new FakeArcRevenueSettlementRecorder
        {
            RecordException = new ArgumentException("invalid split")
        };
        var controller = new ArcRevenueController(service);

        var result = await controller.RecordAsync(CreateRequest(), CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.NotNull(badRequest.Value);
    }

    private const string SettlementId = "0xbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string SignalId = "0xaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    private static ArcRevenueSettlementRequest CreateRequest()
        => new(
            SettlementId,
            ArcRevenueSourceKind.SubscriptionFee,
            SignalId,
            "paper-order-1",
            "0x9000000000000000000000000000000000000009",
            "repricing_lag_arbitrage",
            10m,
            "0x0000000000000000000000000000000000000001",
            Shares: null,
            "demo subscription",
            Simulated: false,
            SourceTransactionHash: "0xcccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc");

    private static ArcRevenueSettlementRecord CreateRecord()
        => new(
            SettlementId,
            ArcRevenueSourceKind.SubscriptionFee,
            SignalId,
            "paper-order-1",
            "0x9000000000000000000000000000000000000009",
            "repricing_lag_arbitrage",
            10m,
            10_000_000,
            "0x0000000000000000000000000000000000000001",
            [
                new ArcRevenueSplitAllocation(
                    ArcRevenueRecipientKind.AgentOwner,
                    "0x1000000000000000000000000000000000000001",
                    7000,
                    7_000_000,
                    7m),
                new ArcRevenueSplitAllocation(
                    ArcRevenueRecipientKind.StrategyAuthor,
                    "0x2000000000000000000000000000000000000002",
                    2000,
                    2_000_000,
                    2m),
                new ArcRevenueSplitAllocation(
                    ArcRevenueRecipientKind.Platform,
                    "0x3000000000000000000000000000000000000003",
                    1000,
                    1_000_000,
                    1m)
            ],
            "demo subscription",
            Simulated: false,
            SourceTransactionHash: "0xcccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc",
            SettlementHash: "0xdddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd",
            TransactionHash: "0xeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee",
            ExplorerUrl: "https://explorer.arc.test/tx/0xeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee",
            ArcRevenueSettlementStatus.Confirmed,
            ErrorCode: null,
            CreatedAtUtc: DateTimeOffset.Parse("2026-05-12T09:59:00Z"),
            RecordedAtUtc: DateTimeOffset.Parse("2026-05-12T10:00:00Z"));

    private sealed class FakeArcRevenueSettlementRecorder : IArcRevenueSettlementRecorder
    {
        public int? LastListLimit { get; private set; }

        public ArcRevenueSettlementRequest? LastRecordRequest { get; private set; }

        public IReadOnlyList<ArcRevenueSettlementRecord> Records { get; init; } = [];

        public ArcRevenueSettlementRecord? GetResult { get; init; }

        public ArcRevenueSettlementResult? RecordResult { get; init; }

        public Exception? RecordException { get; init; }

        public Task<ArcRevenueSettlementResult> RecordAsync(
            ArcRevenueSettlementRequest request,
            CancellationToken cancellationToken = default)
        {
            LastRecordRequest = request;
            if (RecordException is not null)
            {
                throw RecordException;
            }

            return Task.FromResult(RecordResult ?? new ArcRevenueSettlementResult(CreateRecord(), AlreadyRecorded: false));
        }

        public Task<IReadOnlyList<ArcRevenueSettlementRecord>> ListAsync(
            int limit,
            CancellationToken cancellationToken = default)
        {
            LastListLimit = limit;
            return Task.FromResult(Records);
        }

        public Task<ArcRevenueSettlementRecord?> GetAsync(
            string settlementId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(GetResult);
    }
}
