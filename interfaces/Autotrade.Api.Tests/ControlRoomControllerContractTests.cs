using System.Text.Json;
using System.Text.Json.Serialization;
using Autotrade.Api.ControlRoom;
using Autotrade.Api.Controllers;
using Autotrade.Strategy.Application.Contract.Strategies;
using Autotrade.Strategy.Application.Engine;
using Autotrade.Strategy.Application.Parameters;
using Autotrade.Trading.Application.Contract.Execution;
using Autotrade.Trading.Application.Contract.Risk;
using Microsoft.AspNetCore.Mvc;

namespace Autotrade.Api.Tests;

public sealed class ControlRoomControllerContractTests
{
    [Fact]
    public async Task GetSnapshotReturnsFullControlRoomSnapshotContract()
    {
        var snapshot = ControlRoomFixture.CreateSnapshot();
        var queryService = new FakeControlRoomQueryService(snapshot);
        var controller = CreateController(queryService);

        var result = await controller.GetSnapshot(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<ControlRoomSnapshotResponse>(ok.Value);
        Assert.Equal(snapshot.TimestampUtc, response.TimestampUtc);
        Assert.Equal("deterministic", response.DataMode);
        Assert.Equal("paper", response.CommandMode);
        Assert.Equal("Ready", response.Process.ApiStatus);
        Assert.True(response.Risk.Limits.Count > 0);
        Assert.True(response.Strategies.Count > 0);
        Assert.True(response.Markets.Count > 0);
        Assert.True(response.Orders.Count > 0);
        Assert.True(response.Positions.Count > 0);
        Assert.True(response.Decisions.Count > 0);
        Assert.True(response.Timeline.Count > 0);
        Assert.True(response.CapitalCurve.Count > 0);
        Assert.True(response.LatencyCurve.Count > 0);
        Assert.Equal(1, queryService.CallCount);
    }

    [Fact]
    public async Task GetMarketsForwardsDiscoveryQueryAndReturnsPagingContract()
    {
        var marketsResponse = ControlRoomFixture.CreateMarketsResponse();
        var marketDataService = new FakeControlRoomMarketDataService { MarketsResponse = marketsResponse };
        var controller = CreateController(marketDataService: marketDataService);

        var result = await controller.GetMarkets(
            search: "election",
            category: "Politics",
            status: "open",
            sort: "liquidity",
            minLiquidity: 1_000m,
            minVolume24h: 250m,
            maxDaysToExpiry: 30,
            acceptingOrders: true,
            minSignalScore: 0.4m,
            limit: 25,
            offset: 5,
            cancellationToken: CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<ControlRoomMarketsResponse>(ok.Value);
        Assert.Equal("gamma-fixture", response.Source);
        Assert.Equal(1, response.TotalCount);
        Assert.True(response.IsComplete);
        Assert.Contains("Politics", response.Categories);
        Assert.Equal("market-1", response.Markets[0].MarketId);
        Assert.Equal("gamma-fixture", response.Markets[0].Source);
        Assert.True(response.Markets[0].AcceptingOrders);
        Assert.NotNull(response.Markets[0].ExpiresAtUtc);
        Assert.NotNull(marketDataService.LastMarketsQuery);
        Assert.Equal("election", marketDataService.LastMarketsQuery.Search);
        Assert.Equal("Politics", marketDataService.LastMarketsQuery.Category);
        Assert.Equal("open", marketDataService.LastMarketsQuery.Status);
        Assert.Equal("liquidity", marketDataService.LastMarketsQuery.Sort);
        Assert.Equal(1_000m, marketDataService.LastMarketsQuery.MinLiquidity);
        Assert.Equal(250m, marketDataService.LastMarketsQuery.MinVolume24h);
        Assert.Equal(30, marketDataService.LastMarketsQuery.MaxDaysToExpiry);
        Assert.True(marketDataService.LastMarketsQuery.AcceptingOrders);
        Assert.Equal(0.4m, marketDataService.LastMarketsQuery.MinSignalScore);
        Assert.Equal(25, marketDataService.LastMarketsQuery.Limit);
        Assert.Equal(5, marketDataService.LastMarketsQuery.Offset);
    }

    [Fact]
    public async Task GetMarketDetailReturnsMarketDetailWithOrderBookAndRelatedCollections()
    {
        var marketDetail = ControlRoomFixture.CreateMarketDetail();
        var marketDataService = new FakeControlRoomMarketDataService { MarketDetailResponse = marketDetail };
        var controller = CreateController(marketDataService: marketDataService);

        var result = await controller.GetMarketDetail("market-1", levels: 10, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<ControlRoomMarketDetailResponse>(ok.Value);
        Assert.Equal("market-1", response.Market.MarketId);
        Assert.NotNull(response.OrderBook);
        Assert.Equal("token-yes", response.OrderBook.TokenId);
        Assert.True(response.Orders.Count > 0);
        Assert.True(response.Positions.Count > 0);
        Assert.True(response.Decisions.Count > 0);
        Assert.True(response.Microstructure.Count > 0);
        Assert.Equal(("market-1", 10), marketDataService.LastMarketDetailQuery);
    }

    [Fact]
    public async Task GetMarketDetailReturnsNotFoundWhenMarketIsMissing()
    {
        var marketDataService = new FakeControlRoomMarketDataService();
        var controller = CreateController(marketDataService: marketDataService);

        var result = await controller.GetMarketDetail("missing-market", levels: null, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result.Result);
        Assert.Equal(("missing-market", null), marketDataService.LastMarketDetailQuery);
    }

    [Fact]
    public async Task GetOrderBookReturnsOrderBookForSelectedToken()
    {
        var orderBook = ControlRoomFixture.CreateOrderBook();
        var marketDataService = new FakeControlRoomMarketDataService { OrderBookResponse = orderBook };
        var controller = CreateController(marketDataService: marketDataService);

        var result = await controller.GetOrderBook(
            marketId: "market-1",
            tokenId: "token-yes",
            outcome: "YES",
            levels: 12,
            cancellationToken: CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<ControlRoomOrderBookDto>(ok.Value);
        Assert.Equal("market-1", response.MarketId);
        Assert.Equal("token-yes", response.TokenId);
        Assert.Equal("YES", response.Outcome);
        Assert.Equal("book-fixture", response.Source);
        Assert.Equal(ControlRoomOrderBookFreshness.Fresh, response.Freshness.Status);
        Assert.Equal(3, response.Freshness.AgeSeconds);
        Assert.NotEmpty(response.Bids);
        Assert.NotEmpty(response.Asks);
        Assert.Equal(("market-1", "token-yes", "YES", 12), marketDataService.LastOrderBookQuery);
    }

    [Fact]
    public async Task GetOrderBookReturnsNotFoundWhenTokenBookIsMissing()
    {
        var marketDataService = new FakeControlRoomMarketDataService();
        var controller = CreateController(marketDataService: marketDataService);

        var result = await controller.GetOrderBook(
            marketId: "market-1",
            tokenId: "missing-token",
            outcome: "NO",
            levels: null,
            cancellationToken: CancellationToken.None);

        Assert.IsType<NotFoundResult>(result.Result);
        Assert.Equal(("market-1", "missing-token", "NO", null), marketDataService.LastOrderBookQuery);
    }

    [Theory]
    [InlineData("accepted")]
    [InlineData("rejected")]
    [InlineData("disabled")]
    [InlineData("invalid-request")]
    public async Task SetStrategyStateReturnsAcceptedEnvelopeWithServiceDecision(string decisionStatus)
    {
        var commandResponse = ControlRoomFixture.CreateCommandResponse(decisionStatus);
        var commandService = new FakeControlRoomCommandService { StrategyStateResponse = commandResponse };
        var controller = CreateController(commandService: commandService);
        var request = new SetStrategyStateRequest("Paused");

        var result = await controller.SetStrategyState("strategy-main", request, CancellationToken.None);

        var accepted = Assert.IsType<AcceptedResult>(result.Result);
        var response = Assert.IsType<ControlRoomCommandResponse>(accepted.Value);
        Assert.Equal(decisionStatus, response.Status);
        Assert.Equal("paper", response.CommandMode);
        Assert.Equal(("strategy-main", request), commandService.LastStrategyStateCommand);
    }

    [Theory]
    [InlineData("accepted")]
    [InlineData("rejected")]
    [InlineData("disabled")]
    [InlineData("invalid-request")]
    public async Task SetKillSwitchReturnsAcceptedEnvelopeWithServiceDecision(string decisionStatus)
    {
        var commandResponse = ControlRoomFixture.CreateCommandResponse(decisionStatus);
        var commandService = new FakeControlRoomCommandService { KillSwitchResponse = commandResponse };
        var controller = CreateController(commandService: commandService);
        var request = new SetKillSwitchRequest(
            Active: true,
            Level: "Global",
            ReasonCode: "operator-confirmed",
            Reason: "Regression test");

        var result = await controller.SetKillSwitch(request, CancellationToken.None);

        var accepted = Assert.IsType<AcceptedResult>(result.Result);
        var response = Assert.IsType<ControlRoomCommandResponse>(accepted.Value);
        Assert.Equal(decisionStatus, response.Status);
        Assert.Equal("paper", response.CommandMode);
        Assert.Equal(request, commandService.LastKillSwitchCommand);
    }

    [Fact]
    public async Task GetIncidentActionsReturnsActionCatalog()
    {
        var commandService = new FakeControlRoomCommandService();
        var controller = CreateController(commandService: commandService);

        var result = await controller.GetIncidentActions(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<IncidentActionCatalog>(ok.Value);
        Assert.Contains(response.Actions, action => action.Id == "hard-stop");
        Assert.Equal(1, commandService.GetIncidentActionsCallCount);
    }

    [Theory]
    [InlineData("Accepted")]
    [InlineData("Partial")]
    [InlineData("Unsupported")]
    [InlineData("ConfirmationRequired")]
    public async Task CancelOpenOrdersReturnsAcceptedEnvelopeWithServiceDecision(string decisionStatus)
    {
        var commandResponse = ControlRoomFixture.CreateCommandResponse(decisionStatus);
        var commandService = new FakeControlRoomCommandService { CancelOpenOrdersResponse = commandResponse };
        var controller = CreateController(commandService: commandService);
        var request = new CancelOpenOrdersRequest(
            Actor: "operator",
            ReasonCode: "INCIDENT",
            Reason: "regression",
            ConfirmationText: "CONFIRM");

        var result = await controller.CancelOpenOrders(request, CancellationToken.None);

        var accepted = Assert.IsType<AcceptedResult>(result.Result);
        var response = Assert.IsType<ControlRoomCommandResponse>(accepted.Value);
        Assert.Equal(decisionStatus, response.Status);
        Assert.Equal(request, commandService.LastCancelOpenOrdersCommand);
    }

    [Fact]
    public async Task ExportIncidentPackageReturnsPackageWithForwardedQuery()
    {
        var commandService = new FakeControlRoomCommandService();
        var controller = CreateController(commandService: commandService);

        var result = await controller.ExportIncidentPackage(
            riskEventId: "risk-1",
            strategyId: "strategy-main",
            marketId: "market-main",
            orderId: "order-1",
            correlationId: "correlation-1",
            cancellationToken: CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<IncidentPackage>(ok.Value);
        Assert.Equal("risk-1", response.Query.RiskEventId);
        Assert.Equal(new IncidentPackageQuery("risk-1", "strategy-main", "market-main", "order-1", "correlation-1"), commandService.LastIncidentPackageQuery);
    }

    [Fact]
    public async Task GetLiveArmingStatusReturnsCurrentStatus()
    {
        var status = ControlRoomFixture.CreateLiveArmingStatus(isArmed: true);
        var commandService = new FakeControlRoomCommandService { LiveArmingStatus = status };
        var controller = CreateController(commandService: commandService);

        var result = await controller.GetLiveArmingStatus(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<LiveArmingStatus>(ok.Value);
        Assert.True(response.IsArmed);
        Assert.Equal("Armed", response.State);
    }

    [Theory]
    [InlineData("Accepted")]
    [InlineData("Blocked")]
    [InlineData("ConfirmationRequired")]
    public async Task ArmLiveReturnsAcceptedEnvelopeWithServiceDecision(string decisionStatus)
    {
        var commandResponse = ControlRoomFixture.CreateCommandResponse(decisionStatus);
        var commandService = new FakeControlRoomCommandService { ArmLiveResponse = commandResponse };
        var controller = CreateController(commandService: commandService);
        var request = new ArmLiveRequest(Actor: "operator", Reason: "regression", ConfirmationText: "ARM LIVE");

        var result = await controller.ArmLive(request, CancellationToken.None);

        var accepted = Assert.IsType<AcceptedResult>(result.Result);
        var response = Assert.IsType<ControlRoomCommandResponse>(accepted.Value);
        Assert.Equal(decisionStatus, response.Status);
        Assert.Equal(request, commandService.LastArmLiveCommand);
    }

    [Theory]
    [InlineData("Accepted")]
    [InlineData("ConfirmationRequired")]
    public async Task DisarmLiveReturnsAcceptedEnvelopeWithServiceDecision(string decisionStatus)
    {
        var commandResponse = ControlRoomFixture.CreateCommandResponse(decisionStatus);
        var commandService = new FakeControlRoomCommandService { DisarmLiveResponse = commandResponse };
        var controller = CreateController(commandService: commandService);
        var request = new DisarmLiveRequest(Actor: "operator", Reason: "regression", ConfirmationText: "DISARM LIVE");

        var result = await controller.DisarmLive(request, CancellationToken.None);

        var accepted = Assert.IsType<AcceptedResult>(result.Result);
        var response = Assert.IsType<ControlRoomCommandResponse>(accepted.Value);
        Assert.Equal(decisionStatus, response.Status);
        Assert.Equal(request, commandService.LastDisarmLiveCommand);
    }

    [Fact]
    public void ControlRoomContractsSerializeAsWebJsonWithStringEnums()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());

        var json = JsonSerializer.Serialize(ControlRoomFixture.CreateSnapshot(), options);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.True(root.TryGetProperty("timestampUtc", out _));
        Assert.True(root.TryGetProperty("dataMode", out _));
        Assert.True(root.TryGetProperty("commandMode", out _));
        Assert.Equal("Ready", root.GetProperty("process").GetProperty("apiStatus").GetString());
        Assert.Equal("Running", root.GetProperty("strategies")[0].GetProperty("state").GetString());
        Assert.Equal("api-control-room-legacy.v1", root.GetProperty("strategies")[0].GetProperty("modelVersion").GetString());
        Assert.Equal("api-control-room-legacy.v1", root.GetProperty("strategies")[0].GetProperty("sourceVersion").GetString());
        Assert.True(root.GetProperty("risk").TryGetProperty("killSwitchActive", out _));
        Assert.Contains("\"capitalCurve\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"TimestampUtc\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void StrategyControlControllersUseAuditedServiceBoundaries()
    {
        var bannedPorts = new HashSet<Type>
        {
            typeof(IStrategyManager),
            typeof(IRiskManager),
            typeof(IStrategyParameterVersionRepository)
        };
        var controllers = new[]
        {
            typeof(ControlRoomController),
            typeof(StrategyParametersController)
        };

        var offenders = controllers
            .SelectMany(controller => controller.GetConstructors())
            .SelectMany(constructor => constructor.GetParameters()
                .Select(parameter => new { Controller = constructor.DeclaringType!.Name, parameter.ParameterType }))
            .Where(item => bannedPorts.Contains(item.ParameterType))
            .Select(item => $"{item.Controller}:{item.ParameterType.Name}")
            .ToArray();

        Assert.Empty(offenders);
    }

    private static ControlRoomController CreateController(
        IControlRoomQueryService? queryService = null,
        IControlRoomMarketDataService? marketDataService = null,
        IControlRoomCommandService? commandService = null)
    {
        return new ControlRoomController(
            queryService ?? new FakeControlRoomQueryService(ControlRoomFixture.CreateSnapshot()),
            marketDataService ?? new FakeControlRoomMarketDataService(),
            commandService ?? new FakeControlRoomCommandService());
    }

    private sealed class FakeControlRoomQueryService(ControlRoomSnapshotResponse snapshot) : IControlRoomQueryService
    {
        public int CallCount { get; private set; }

        public Task<ControlRoomSnapshotResponse> GetSnapshotAsync(CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(snapshot);
        }
    }

    private sealed class FakeControlRoomMarketDataService : IControlRoomMarketDataService
    {
        public ControlRoomMarketsResponse MarketsResponse { get; init; } = ControlRoomFixture.CreateMarketsResponse();

        public ControlRoomMarketDetailResponse? MarketDetailResponse { get; init; }

        public ControlRoomOrderBookDto? OrderBookResponse { get; init; }

        public ControlRoomMarketDiscoveryQuery? LastMarketsQuery { get; private set; }

        public (string MarketId, int? Levels)? LastMarketDetailQuery { get; private set; }

        public (string MarketId, string? TokenId, string? Outcome, int? Levels)? LastOrderBookQuery { get; private set; }

        public Task<ControlRoomMarketsResponse> GetMarketsAsync(
            ControlRoomMarketDiscoveryQuery query,
            CancellationToken cancellationToken = default)
        {
            LastMarketsQuery = query;
            return Task.FromResult(MarketsResponse);
        }

        public Task<ControlRoomMarketDetailResponse?> GetMarketDetailAsync(
            string marketId,
            int? levels,
            CancellationToken cancellationToken = default)
        {
            LastMarketDetailQuery = (marketId, levels);
            return Task.FromResult(MarketDetailResponse);
        }

        public Task<ControlRoomOrderBookDto?> GetOrderBookAsync(
            string marketId,
            string? tokenId,
            string? outcome,
            int? levels,
            CancellationToken cancellationToken = default)
        {
            LastOrderBookQuery = (marketId, tokenId, outcome, levels);
            return Task.FromResult(OrderBookResponse);
        }
    }

    private sealed class FakeControlRoomCommandService : IControlRoomCommandService
    {
        public ControlRoomCommandResponse StrategyStateResponse { get; init; } = ControlRoomFixture.CreateCommandResponse("accepted");

        public ControlRoomCommandResponse KillSwitchResponse { get; init; } = ControlRoomFixture.CreateCommandResponse("accepted");

        public IncidentActionCatalog IncidentActionCatalog { get; init; } = ControlRoomFixture.CreateIncidentActionCatalog();

        public ControlRoomCommandResponse CancelOpenOrdersResponse { get; init; } = ControlRoomFixture.CreateCommandResponse("accepted");

        public IncidentPackage IncidentPackage { get; init; } = ControlRoomFixture.CreateIncidentPackage();

        public LiveArmingStatus LiveArmingStatus { get; init; } = ControlRoomFixture.CreateLiveArmingStatus(isArmed: false);

        public ControlRoomCommandResponse ArmLiveResponse { get; init; } = ControlRoomFixture.CreateCommandResponse("accepted");

        public ControlRoomCommandResponse DisarmLiveResponse { get; init; } = ControlRoomFixture.CreateCommandResponse("accepted");

        public (string StrategyId, SetStrategyStateRequest Request)? LastStrategyStateCommand { get; private set; }

        public SetKillSwitchRequest? LastKillSwitchCommand { get; private set; }

        public int GetIncidentActionsCallCount { get; private set; }

        public CancelOpenOrdersRequest? LastCancelOpenOrdersCommand { get; private set; }

        public IncidentPackageQuery? LastIncidentPackageQuery { get; private set; }

        public ArmLiveRequest? LastArmLiveCommand { get; private set; }

        public DisarmLiveRequest? LastDisarmLiveCommand { get; private set; }

        public Task<LiveArmingStatus> GetLiveArmingStatusAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(LiveArmingStatus);
        }

        public Task<ControlRoomCommandResponse> SetStrategyStateAsync(
            string strategyId,
            SetStrategyStateRequest request,
            CancellationToken cancellationToken = default)
        {
            LastStrategyStateCommand = (strategyId, request);
            return Task.FromResult(StrategyStateResponse);
        }

        public Task<ControlRoomCommandResponse> SetKillSwitchAsync(
            SetKillSwitchRequest request,
            CancellationToken cancellationToken = default)
        {
            LastKillSwitchCommand = request;
            return Task.FromResult(KillSwitchResponse);
        }

        public Task<IncidentActionCatalog> GetIncidentActionsAsync(CancellationToken cancellationToken = default)
        {
            GetIncidentActionsCallCount++;
            return Task.FromResult(IncidentActionCatalog);
        }

        public Task<ControlRoomCommandResponse> CancelOpenOrdersAsync(
            CancelOpenOrdersRequest request,
            CancellationToken cancellationToken = default)
        {
            LastCancelOpenOrdersCommand = request;
            return Task.FromResult(CancelOpenOrdersResponse);
        }

        public Task<IncidentPackage> ExportIncidentPackageAsync(
            IncidentPackageQuery query,
            CancellationToken cancellationToken = default)
        {
            LastIncidentPackageQuery = query;
            return Task.FromResult(IncidentPackage with { Query = query });
        }

        public Task<ControlRoomCommandResponse> ArmLiveAsync(
            ArmLiveRequest request,
            CancellationToken cancellationToken = default)
        {
            LastArmLiveCommand = request;
            return Task.FromResult(ArmLiveResponse);
        }

        public Task<ControlRoomCommandResponse> DisarmLiveAsync(
            DisarmLiveRequest request,
            CancellationToken cancellationToken = default)
        {
            LastDisarmLiveCommand = request;
            return Task.FromResult(DisarmLiveResponse);
        }
    }

    private static class ControlRoomFixture
    {
        private static readonly DateTimeOffset Now = new(2026, 5, 3, 8, 30, 0, TimeSpan.Zero);

        public static ControlRoomSnapshotResponse CreateSnapshot()
        {
            return new ControlRoomSnapshotResponse(
                TimestampUtc: Now,
                DataMode: "deterministic",
                CommandMode: "paper",
                Process: new ControlRoomProcessDto(
                    ApiStatus: "Ready",
                    Environment: "Test",
                    ExecutionMode: "Paper",
                    ModulesEnabled: true,
                    ReadyChecks: 4,
                    DegradedChecks: 0,
                    UnhealthyChecks: 0),
                Risk: new ControlRoomRiskDto(
                    KillSwitchActive: false,
                    KillSwitchLevel: "None",
                    KillSwitchReason: null,
                    KillSwitchActivatedAtUtc: null,
                    TotalCapital: 10_000m,
                    AvailableCapital: 7_500m,
                    CapitalUtilizationPct: 25m,
                    OpenNotional: 2_500m,
                    OpenOrders: 2,
                    UnhedgedExposures: 1,
                    Limits:
                    [
                        new ControlRoomRiskLimitDto("PerMarketNotional", 250m, 500m, "USDC", "ok")
                    ]),
                Metrics:
                [
                    new ControlRoomMetricDto("Latency", "12 ms", "-3 ms", "good")
                ],
                Strategies:
                [
                    new ControlRoomStrategyDto(
                        StrategyId: "strategy-main",
                        Name: "Repricing Lag",
                        State: StrategyState.Running,
                        Enabled: true,
                        ConfigVersion: "cfg-001",
                        DesiredState: "Running",
                        ActiveMarkets: 3,
                        CycleCount: 44,
                        SnapshotsProcessed: 1024,
                        ChannelBacklog: 0,
                        IsKillSwitchBlocked: false,
                        LastHeartbeatUtc: Now.AddSeconds(-5),
                        LastDecisionAtUtc: Now.AddSeconds(-15),
                        LastError: null,
                        BlockedReason: null,
                        Parameters:
                        [
                            new ControlRoomParameterDto("MaxSpreadBps", "250")
                        ])
                ],
                Markets:
                [
                    CreateMarket()
                ],
                Orders:
                [
                    new ControlRoomOrderDto(
                        ClientOrderId: "order-1",
                        StrategyId: "strategy-main",
                        MarketId: "market-1",
                        Side: "Buy",
                        Outcome: "YES",
                        Price: 0.47m,
                        Quantity: 20m,
                        FilledQuantity: 5m,
                        Status: "Open",
                        UpdatedAtUtc: Now.AddSeconds(-20))
                ],
                Positions:
                [
                    new ControlRoomPositionDto(
                        MarketId: "market-1",
                        Outcome: "YES",
                        Quantity: 25m,
                        AverageCost: 0.44m,
                        Notional: 11m,
                        RealizedPnl: 1.25m,
                        MarkPrice: 0.48m,
                        UnrealizedPnl: 1m,
                        TotalPnl: 2.25m,
                        ReturnPct: 20.45m,
                        MarkSource: "midpoint",
                        UpdatedAtUtc: Now.AddSeconds(-12))
                ],
                Decisions:
                [
                    new ControlRoomDecisionDto(
                        StrategyId: "strategy-main",
                        Action: "QuoteAdjusted",
                        MarketId: "market-1",
                        Reason: "Spread widened",
                        CreatedAtUtc: Now.AddSeconds(-30))
                ],
                Timeline:
                [
                    new ControlRoomTimelineItemDto(
                        TimestampUtc: Now.AddMinutes(-1),
                        Label: "Cycle completed",
                        Detail: "Processed one market snapshot",
                        Tone: "info")
                ],
                CapitalCurve:
                [
                    new ControlRoomSeriesPointDto(Now.AddMinutes(-2), 9_950m),
                    new ControlRoomSeriesPointDto(Now, 10_000m)
                ],
                LatencyCurve:
                [
                    new ControlRoomSeriesPointDto(Now.AddMinutes(-2), 15m),
                    new ControlRoomSeriesPointDto(Now, 12m)
                ]);
        }

        public static ControlRoomMarketsResponse CreateMarketsResponse()
        {
            return new ControlRoomMarketsResponse(
                TimestampUtc: Now,
                Source: "gamma-fixture",
                TotalCount: 1,
                IsComplete: true,
                Categories:
                [
                    "Politics"
                ],
                Markets:
                [
                    CreateMarket()
                ]);
        }

        public static ControlRoomMarketDetailResponse CreateMarketDetail()
        {
            return new ControlRoomMarketDetailResponse(
                TimestampUtc: Now,
                Source: "detail-fixture",
                Market: CreateMarket(),
                OrderBook: CreateOrderBook(),
                Orders: CreateSnapshot().Orders,
                Positions: CreateSnapshot().Positions,
                Decisions: CreateSnapshot().Decisions,
                Microstructure:
                [
                    new ControlRoomMetricDto("Spread", "3.0 c", "0.5 c", "warn")
                ]);
        }

        public static ControlRoomOrderBookDto CreateOrderBook()
        {
            return new ControlRoomOrderBookDto(
                MarketId: "market-1",
                TokenId: "token-yes",
                Outcome: "YES",
                LastUpdatedUtc: Now.AddSeconds(-3),
                BestBidPrice: 0.47m,
                BestBidSize: 42m,
                BestAskPrice: 0.50m,
                BestAskSize: 38m,
                Spread: 0.03m,
                Midpoint: 0.485m,
                TotalBidSize: 300m,
                TotalAskSize: 280m,
                ImbalancePct: 3.45m,
                MaxLevelNotional: 150m,
                Source: "book-fixture",
                Freshness: new ControlRoomOrderBookFreshnessDto(
                    ControlRoomOrderBookFreshness.Fresh,
                    3,
                    5,
                    30,
                    "Order book updated 3s ago."),
                Bids:
                [
                    new ControlRoomOrderBookLevelDto(1, 0.47m, 42m, 19.74m, 14m)
                ],
                Asks:
                [
                    new ControlRoomOrderBookLevelDto(1, 0.50m, 38m, 19.00m, 13.57m)
                ]);
        }

        public static ControlRoomCommandResponse CreateCommandResponse(string status)
        {
            return new ControlRoomCommandResponse(
                Status: status,
                CommandMode: "paper",
                Message: $"Command {status}.",
                Snapshot: CreateSnapshot());
        }

        public static IncidentActionCatalog CreateIncidentActionCatalog()
        {
            return new IncidentActionCatalog(
                GeneratedAtUtc: Now,
                CommandMode: "paper",
                RunbookPath: "docs/operations/autotrade-incident-runbook.md",
                Actions:
                [
                    new IncidentActionDescriptor(
                        "hard-stop",
                        "Hard stop",
                        "Risk",
                        "Global",
                        "POST",
                        "/api/control-room/risk/kill-switch",
                        true,
                        null,
                        "CONFIRM",
                        "fixture")
                ]);
        }

        public static IncidentPackage CreateIncidentPackage()
        {
            return new IncidentPackage(
                GeneratedAtUtc: Now,
                ContractVersion: "control-room-incident-package.v1",
                Query: new IncidentPackageQuery(),
                Snapshot: CreateSnapshot(),
                Actions: CreateIncidentActionCatalog(),
                RunbookReferences:
                [
                    "docs/operations/autotrade-incident-runbook.md"
                ],
                ExportReferences:
                [
                    "/api/control-room/snapshot"
                ]);
        }

        public static LiveArmingStatus CreateLiveArmingStatus(bool isArmed)
        {
            var now = new DateTimeOffset(2026, 5, 3, 8, 30, 0, TimeSpan.Zero);
            var evidence = isArmed
                ? new LiveArmingEvidence(
                    "evidence-1",
                    "operator",
                    "regression",
                    now.AddMinutes(-5),
                    now.AddHours(4),
                    "test",
                    "fingerprint",
                    new LiveArmingRiskSummary(100m, 80m, 20m, 10m, 1, 0, false),
                    ["risk.limits.configured"])
                : null;

            return new LiveArmingStatus(
                isArmed,
                isArmed ? "Armed" : "NotArmed",
                isArmed ? "Live trading is armed." : "Live arming evidence has not been recorded.",
                "test",
                now,
                evidence,
                isArmed ? [] : ["Live arming evidence has not been recorded."]);
        }

        private static ControlRoomMarketDto CreateMarket()
        {
            return new ControlRoomMarketDto(
                MarketId: "market-1",
                ConditionId: "condition-1",
                Name: "Will the fixture market settle yes?",
                Category: "Politics",
                Status: "open",
                YesPrice: 0.48m,
                NoPrice: 0.52m,
                Liquidity: 25_000m,
                Volume24h: 3_500m,
                ExpiresAtUtc: Now.AddDays(10),
                SignalScore: 0.62m,
                Slug: "fixture-market",
                Description: "Deterministic fixture market.",
                AcceptingOrders: true,
                Tokens:
                [
                    new ControlRoomMarketTokenDto("token-yes", "YES", 0.48m, null),
                    new ControlRoomMarketTokenDto("token-no", "NO", 0.52m, null)
                ],
                Tags:
                [
                    "fixture",
                    "contract"
                ],
                Spread: 0.04m,
                Source: "gamma-fixture",
                RankScore: 0.72m,
                RankReason: "moderate signal; deep liquidity; some 24h volume; accepting orders; expires in 10d",
                UnsuitableReasons: Array.Empty<string>());
        }
    }
}
