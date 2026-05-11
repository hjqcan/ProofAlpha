using Autotrade.Api.Controllers;
using Autotrade.Api.ControlRoom;
using Autotrade.Strategy.Application.Parameters;
using Autotrade.Trading.Application.Contract.Execution;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Autotrade.Api.Tests;

public sealed class StrategyParametersControllerContractTests
{
    [Fact]
    public async Task GetSnapshotReturnsCurrentParametersAndRecentVersions()
    {
        var service = new FakeStrategyParameterVersionService();
        var controller = CreateController(service);

        var result = await controller.GetSnapshot("liquidity_pulse", limit: 5, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<StrategyParameterSnapshot>(ok.Value);
        Assert.Equal("liquidity_pulse", response.StrategyId);
        Assert.Equal("v1", response.ConfigVersion);
        Assert.Single(response.Parameters);
        Assert.Equal(5, service.LastLimit);
    }

    [Fact]
    public async Task UpdateForwardsChangesAndReturnsAcceptedMutation()
    {
        var service = new FakeStrategyParameterVersionService();
        var controller = CreateController(service);

        var result = await controller.Update(
            "liquidity_pulse",
            new StrategyParameterUpdateRequest(
                new Dictionary<string, string> { ["MaxMarkets"] = "12" },
                "operator-1",
                "tighten",
                InvalidateLiveArming: false,
                LiveDisarmConfirmationText: null),
            CancellationToken.None);

        var accepted = Assert.IsType<AcceptedResult>(result.Result);
        var response = Assert.IsType<StrategyParameterMutationResult>(accepted.Value);
        Assert.True(response.Accepted);
        Assert.Equal("liquidity_pulse", service.LastUpdateStrategyId);
        Assert.Equal("12", service.LastUpdateRequest?.Changes["MaxMarkets"]);
        Assert.Equal("control-room-api", service.LastUpdateRequest?.Source);
    }

    [Fact]
    public async Task UpdateRejectsWhenLiveServicesIsArmedAndInvalidationWasNotConfirmed()
    {
        var service = new FakeStrategyParameterVersionService();
        var liveArming = new FakeLiveArmingService(isArmed: true);
        var controller = CreateController(
            service,
            liveArming,
            new ControlRoomOptions
            {
                EnableControlCommands = true,
                CommandMode = ControlRoomCommandModes.LiveServices
            });

        var result = await controller.Update(
            "liquidity_pulse",
            new StrategyParameterUpdateRequest(
                new Dictionary<string, string> { ["MaxMarkets"] = "12" },
                "operator-1",
                "tighten",
                InvalidateLiveArming: false,
                LiveDisarmConfirmationText: null),
            CancellationToken.None);

        Assert.IsType<ConflictObjectResult>(result.Result);
        Assert.Null(service.LastUpdateStrategyId);
    }

    [Fact]
    public async Task RollbackDisarmsLiveBeforeForwardingMutationWhenConfirmed()
    {
        var service = new FakeStrategyParameterVersionService();
        var liveArming = new FakeLiveArmingService(isArmed: true);
        var controller = CreateController(
            service,
            liveArming,
            new ControlRoomOptions
            {
                EnableControlCommands = true,
                CommandMode = ControlRoomCommandModes.LiveServices
            });
        var versionId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

        var result = await controller.Rollback(
            "liquidity_pulse",
            versionId,
            new StrategyParameterRollbackApiRequest(
                "operator-1",
                "rollback",
                InvalidateLiveArming: true,
                LiveDisarmConfirmationText: "DISARM LIVE"),
            CancellationToken.None);

        Assert.IsType<AcceptedResult>(result.Result);
        Assert.True(liveArming.DisarmCalled);
        Assert.Equal(versionId, service.LastRollbackRequest?.VersionId);
    }

    private static StrategyParametersController CreateController(
        FakeStrategyParameterVersionService service,
        ILiveArmingService? liveArmingService = null,
        ControlRoomOptions? options = null)
    {
        var services = new ServiceCollection();
        if (liveArmingService is not null)
        {
            services.AddSingleton(liveArmingService);
        }

        return new StrategyParametersController(
            service,
            services.BuildServiceProvider(),
            new TestOptionsMonitor<ControlRoomOptions>(options ?? new ControlRoomOptions()));
    }

    private sealed class FakeStrategyParameterVersionService : IStrategyParameterVersionService
    {
        public int? LastLimit { get; private set; }
        public string? LastUpdateStrategyId { get; private set; }
        public StrategyParameterMutationRequest? LastUpdateRequest { get; private set; }
        public StrategyParameterRollbackRequest? LastRollbackRequest { get; private set; }

        public Task<StrategyParameterSnapshot> GetSnapshotAsync(
            string strategyId,
            int limit = 10,
            CancellationToken cancellationToken = default)
        {
            LastLimit = limit;
            return Task.FromResult(CreateSnapshot(strategyId));
        }

        public Task<StrategyParameterMutationResult> UpdateAsync(
            string strategyId,
            StrategyParameterMutationRequest request,
            CancellationToken cancellationToken = default)
        {
            LastUpdateStrategyId = strategyId;
            LastUpdateRequest = request;
            return Task.FromResult(CreateMutationResult(strategyId));
        }

        public Task<StrategyParameterMutationResult> RollbackAsync(
            string strategyId,
            StrategyParameterRollbackRequest request,
            CancellationToken cancellationToken = default)
        {
            LastRollbackRequest = request;
            return Task.FromResult(CreateMutationResult(strategyId));
        }

        public Task ApplyLatestAcceptedVersionsAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        private static StrategyParameterMutationResult CreateMutationResult(string strategyId)
        {
            var snapshot = CreateSnapshot(strategyId);
            return new StrategyParameterMutationResult(
                true,
                "Accepted",
                "updated",
                null,
                snapshot);
        }

        private static StrategyParameterSnapshot CreateSnapshot(string strategyId)
        {
            return new StrategyParameterSnapshot(
                strategyId,
                "v1",
                [new StrategyParameterValue("MaxMarkets", "40", "int", true)],
                []);
        }
    }

    private sealed class FakeLiveArmingService(bool isArmed) : ILiveArmingService
    {
        public bool DisarmCalled { get; private set; }

        public Task<LiveArmingStatus> GetStatusAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(CreateStatus(isArmed));
        }

        public Task<LiveArmingResult> ArmAsync(
            LiveArmingRequest request,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new LiveArmingResult(true, "Accepted", "armed", CreateStatus(true)));
        }

        public Task<LiveArmingResult> DisarmAsync(
            LiveDisarmingRequest request,
            CancellationToken cancellationToken = default)
        {
            DisarmCalled = true;
            var accepted = request.ConfirmationText == "DISARM LIVE";
            return Task.FromResult(new LiveArmingResult(
                accepted,
                accepted ? "Accepted" : "ConfirmationRequired",
                accepted ? "disarmed" : "confirmation required",
                CreateStatus(!accepted)));
        }

        public Task<LiveArmingStatus> RequireArmedAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(CreateStatus(isArmed));
        }

        private static LiveArmingStatus CreateStatus(bool armed)
        {
            return new LiveArmingStatus(
                armed,
                armed ? "Armed" : "NotArmed",
                armed ? "armed" : "not armed",
                "v1",
                DateTimeOffset.UtcNow,
                null,
                armed ? [] : ["not armed"]);
        }
    }
}
