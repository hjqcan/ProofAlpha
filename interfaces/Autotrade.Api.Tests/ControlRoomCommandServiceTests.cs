using System.Text.Json;
using Autotrade.Application.DTOs;
using Autotrade.Api.ControlRoom;
using Autotrade.ArcSettlement.Application.Contract.Access;
using Autotrade.Strategy.Application.Audit;
using Autotrade.Strategy.Application.Contract.Strategies;
using Autotrade.Strategy.Application.Engine;
using Autotrade.Trading.Application.Audit;
using Autotrade.Trading.Application.Contract.Execution;
using Autotrade.Trading.Application.Contract.Repositories;
using Autotrade.Trading.Application.Contract.Risk;
using Autotrade.Trading.Domain.Shared.Enums;
using Microsoft.Extensions.DependencyInjection;

namespace Autotrade.Api.Tests;

public sealed class ControlRoomCommandServiceTests
{
    [Fact]
    public async Task SetStrategyStateReturnsDisabledWhenCommandsAreDisabledByConfiguration()
    {
        var strategyManager = new FakeStrategyManager();
        var service = CreateService(
            new ControlRoomOptions
            {
                EnableControlCommands = false,
                CommandMode = ControlRoomCommandModes.LiveServices
            },
            strategyManager: strategyManager);

        var response = await service.SetStrategyStateAsync(
            "strategy-main",
            new SetStrategyStateRequest("Paused"),
            CancellationToken.None);

        Assert.Equal("Disabled", response.Status);
        Assert.Equal(ControlRoomCommandModes.ReadOnly, response.CommandMode);
        Assert.Equal(0, strategyManager.SetDesiredStateCallCount);
    }

    [Fact]
    public async Task SetStrategyStateReturnsDisabledWhenCommandModeIsReadOnly()
    {
        var strategyManager = new FakeStrategyManager();
        var service = CreateService(
            new ControlRoomOptions
            {
                EnableControlCommands = true,
                CommandMode = ControlRoomCommandModes.ReadOnly
            },
            strategyManager: strategyManager);

        var response = await service.SetStrategyStateAsync(
            "strategy-main",
            new SetStrategyStateRequest("Stopped"),
            CancellationToken.None);

        Assert.Equal("Disabled", response.Status);
        Assert.Equal(ControlRoomCommandModes.ReadOnly, response.CommandMode);
        Assert.Equal(0, strategyManager.SetDesiredStateCallCount);
    }

    [Fact]
    public async Task SetStrategyStateReturnsInvalidRequestForUnsupportedTargetWithoutCallingManager()
    {
        var strategyManager = new FakeStrategyManager();
        var service = CreateService(
            new ControlRoomOptions
            {
                EnableControlCommands = true,
                CommandMode = ControlRoomCommandModes.Paper
            },
            strategyManager: strategyManager);

        var response = await service.SetStrategyStateAsync(
            "strategy-main",
            new SetStrategyStateRequest("Created"),
            CancellationToken.None);

        Assert.Equal("InvalidRequest", response.Status);
        Assert.Equal(ControlRoomCommandModes.Paper, response.CommandMode);
        Assert.Equal(0, strategyManager.SetDesiredStateCallCount);
    }

    [Fact]
    public async Task SetStrategyStateCallsManagerWhenPaperCommandsAreEnabled()
    {
        var strategyManager = new FakeStrategyManager();
        var service = CreateService(
            new ControlRoomOptions
            {
                EnableControlCommands = true,
                CommandMode = ControlRoomCommandModes.Paper
            },
            strategyManager: strategyManager);

        var response = await service.SetStrategyStateAsync(
            "strategy-main",
            new SetStrategyStateRequest("Paused"),
            CancellationToken.None);

        Assert.Equal("Accepted", response.Status);
        Assert.Equal(ControlRoomCommandModes.Paper, response.CommandMode);
        Assert.Equal(1, strategyManager.SetDesiredStateCallCount);
        Assert.Equal(("strategy-main", StrategyState.Paused), strategyManager.LastDesiredState);
    }

    [Fact]
    public async Task SetStrategyStateRequiresConfirmationBeforeLiveRunning()
    {
        var strategyManager = new FakeStrategyManager();
        var auditLogger = new FakeCommandAuditLogger();
        var service = CreateService(
            new ControlRoomOptions
            {
                EnableControlCommands = true,
                CommandMode = ControlRoomCommandModes.LiveServices
            },
            strategyManager: strategyManager,
            auditLogger: auditLogger);

        var response = await service.SetStrategyStateAsync(
            "strategy-main",
            new SetStrategyStateRequest("Running", Actor: "operator"),
            CancellationToken.None);

        Assert.Equal("ConfirmationRequired", response.Status);
        Assert.Equal(0, strategyManager.SetDesiredStateCallCount);
        var audit = Assert.Single(auditLogger.Entries);
        Assert.False(audit.Success);
        Assert.Equal(3, audit.ExitCode);
        Assert.Equal("operator", audit.Actor);
        using var payload = JsonDocument.Parse(audit.ArgumentsJson);
        Assert.Equal("ConfirmationRequired", payload.RootElement.GetProperty("outcome").GetString());
        Assert.True(payload.RootElement.GetProperty("confirmationRequired").GetBoolean());
        Assert.Equal("Running", payload.RootElement.GetProperty("targetState").GetString());
    }

    [Fact]
    public async Task SetStrategyStateAuditsPreviousTargetAndOutcomeWhenAccepted()
    {
        var strategyManager = new FakeStrategyManager();
        var auditLogger = new FakeCommandAuditLogger();
        var service = CreateService(
            new ControlRoomOptions
            {
                EnableControlCommands = true,
                CommandMode = ControlRoomCommandModes.Paper
            },
            strategyManager: strategyManager,
            auditLogger: auditLogger);

        var response = await service.SetStrategyStateAsync(
            "strategy-main",
            new SetStrategyStateRequest("Paused", Actor: "operator", ReasonCode: "test", Reason: "regression"),
            CancellationToken.None);

        Assert.Equal("Accepted", response.Status);
        var audit = Assert.Single(auditLogger.Entries);
        Assert.True(audit.Success);
        Assert.Equal(0, audit.ExitCode);
        Assert.Equal("operator", audit.Actor);
        using var payload = JsonDocument.Parse(audit.ArgumentsJson);
        Assert.Equal("strategy-main", payload.RootElement.GetProperty("strategyId").GetString());
        Assert.Equal("Running", payload.RootElement.GetProperty("previousState").GetString());
        Assert.Equal("Paused", payload.RootElement.GetProperty("targetState").GetString());
        Assert.Equal("Accepted", payload.RootElement.GetProperty("outcome").GetString());
        Assert.Equal("test", payload.RootElement.GetProperty("reasonCode").GetString());
    }

    [Fact]
    public async Task SetStrategyStateAuditsRejectedManagerFailure()
    {
        var strategyManager = new FakeStrategyManager
        {
            SetDesiredStateException = new InvalidOperationException("manager rejected command")
        };
        var auditLogger = new FakeCommandAuditLogger();
        var service = CreateService(
            new ControlRoomOptions
            {
                EnableControlCommands = true,
                CommandMode = ControlRoomCommandModes.Paper
            },
            strategyManager: strategyManager,
            auditLogger: auditLogger);

        var response = await service.SetStrategyStateAsync(
            "strategy-main",
            new SetStrategyStateRequest("Paused", Actor: "operator"),
            CancellationToken.None);

        Assert.Equal("Rejected", response.Status);
        var audit = Assert.Single(auditLogger.Entries);
        Assert.False(audit.Success);
        Assert.Equal(1, audit.ExitCode);
        using var payload = JsonDocument.Parse(audit.ArgumentsJson);
        Assert.Equal("Rejected", payload.RootElement.GetProperty("outcome").GetString());
        Assert.Equal("manager rejected command", payload.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task RequestArcPaperAutoTradeDeniesWhenEntitlementIsMissing()
    {
        var strategyManager = new FakeStrategyManager();
        var auditLogger = new FakeCommandAuditLogger();
        var access = new FakeArcAccessDecisionService(CreateArcDecision(allowed: false, "ACCESS_NOT_FOUND"));
        var service = CreateService(
            new ControlRoomOptions
            {
                EnableControlCommands = true,
                CommandMode = ControlRoomCommandModes.Paper
            },
            strategyManager: strategyManager,
            auditLogger: auditLogger,
            accessDecisionService: access);

        var response = await service.RequestArcPaperAutoTradeAsync(
            "strategy-main",
            new ArcPaperAutoTradeRequest(WalletAddress: Wallet, Actor: "subscriber"),
            CancellationToken.None);

        Assert.Equal("AccessDenied", response.Status);
        Assert.False(response.AccessDecision.Allowed);
        Assert.Equal(0, strategyManager.SetDesiredStateCallCount);
        var audit = Assert.Single(auditLogger.Entries);
        Assert.False(audit.Success);
        using var payload = JsonDocument.Parse(audit.ArgumentsJson);
        Assert.Equal("ACCESS_NOT_FOUND", payload.RootElement.GetProperty("accessReasonCode").GetString());
    }

    [Fact]
    public async Task RequestArcPaperAutoTradeBlocksLiveServicesCommandMode()
    {
        var strategyManager = new FakeStrategyManager();
        var service = CreateService(
            new ControlRoomOptions
            {
                EnableControlCommands = true,
                CommandMode = ControlRoomCommandModes.LiveServices
            },
            strategyManager: strategyManager,
            accessDecisionService: new FakeArcAccessDecisionService(CreateArcDecision(allowed: true, "ACCESS_ALLOWED")));

        var response = await service.RequestArcPaperAutoTradeAsync(
            "strategy-main",
            new ArcPaperAutoTradeRequest(WalletAddress: Wallet),
            CancellationToken.None);

        Assert.Equal("LiveTradingBlocked", response.Status);
        Assert.Equal(0, strategyManager.SetDesiredStateCallCount);
    }

    [Fact]
    public async Task RequestArcPaperAutoTradeRequiresStrategyInSnapshot()
    {
        var strategyManager = new FakeStrategyManager();
        var service = CreateService(
            new ControlRoomOptions
            {
                EnableControlCommands = true,
                CommandMode = ControlRoomCommandModes.Paper
            },
            strategyManager: strategyManager,
            accessDecisionService: new FakeArcAccessDecisionService(CreateArcDecision(allowed: true, "ACCESS_ALLOWED")),
            queryService: new FakeControlRoomQueryService
            {
                Snapshot = CreateSnapshot(openOrders: 0, killSwitchActive: false, includeStrategy: false)
            });

        var response = await service.RequestArcPaperAutoTradeAsync(
            "missing-strategy",
            new ArcPaperAutoTradeRequest(WalletAddress: Wallet),
            CancellationToken.None);

        Assert.Equal("StrategyNotFound", response.Status);
        Assert.Equal(0, strategyManager.SetDesiredStateCallCount);
    }

    [Fact]
    public async Task RequestArcPaperAutoTradeStartsStrategyThroughExistingStateCommand()
    {
        var strategyManager = new FakeStrategyManager();
        var auditLogger = new FakeCommandAuditLogger();
        var service = CreateService(
            new ControlRoomOptions
            {
                EnableControlCommands = true,
                CommandMode = ControlRoomCommandModes.Paper
            },
            strategyManager: strategyManager,
            auditLogger: auditLogger,
            accessDecisionService: new FakeArcAccessDecisionService(CreateArcDecision(allowed: true, "ACCESS_ALLOWED")),
            queryService: new FakeControlRoomQueryService
            {
                Snapshot = CreateSnapshot(openOrders: 0, killSwitchActive: false, includeStrategy: true)
            });

        var response = await service.RequestArcPaperAutoTradeAsync(
            "strategy-main",
            new ArcPaperAutoTradeRequest(WalletAddress: Wallet, Actor: "subscriber", Reason: "arc demo"),
            CancellationToken.None);

        Assert.Equal("Accepted", response.Status);
        Assert.NotNull(response.Command);
        Assert.Equal(("strategy-main", StrategyState.Running), strategyManager.LastDesiredState);
        Assert.Equal(2, auditLogger.Entries.Count);
        var arcAudit = auditLogger.Entries.Single(entry => entry.CommandName == "arc paper autotrade permission");
        Assert.True(arcAudit.Success);
        using var payload = JsonDocument.Parse(arcAudit.ArgumentsJson);
        Assert.Equal("ACCESS_ALLOWED", payload.RootElement.GetProperty("accessReasonCode").GetString());
        Assert.Equal("0xaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", payload.RootElement.GetProperty("accessEvidenceTransactionHash").GetString());
    }

    [Fact]
    public async Task SetStrategyStateWritesAuditBeforeSnapshotFailureCanEscape()
    {
        var strategyManager = new FakeStrategyManager();
        var auditLogger = new FakeCommandAuditLogger();
        var service = CreateService(
            new ControlRoomOptions
            {
                EnableControlCommands = true,
                CommandMode = ControlRoomCommandModes.Paper
            },
            strategyManager: strategyManager,
            auditLogger: auditLogger,
            queryService: new FakeControlRoomQueryService
            {
                SnapshotException = new InvalidOperationException("snapshot failed")
            });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SetStrategyStateAsync(
                "strategy-main",
                new SetStrategyStateRequest("Paused", Actor: "operator"),
                CancellationToken.None));

        Assert.Equal("snapshot failed", exception.Message);
        var audit = Assert.Single(auditLogger.Entries);
        Assert.True(audit.Success);
        using var payload = JsonDocument.Parse(audit.ArgumentsJson);
        Assert.Equal("Accepted", payload.RootElement.GetProperty("outcome").GetString());
    }

    [Theory]
    [MemberData(nameof(StrategyStateAuditCases))]
    public async Task SetStrategyStateAlwaysAuditsTerminalResponses(
        string expectedStatus,
        ControlRoomOptions options,
        SetStrategyStateRequest request,
        Exception? managerException)
    {
        var strategyManager = new FakeStrategyManager
        {
            SetDesiredStateException = managerException
        };
        var auditLogger = new FakeCommandAuditLogger();
        var service = CreateService(
            options,
            strategyManager: strategyManager,
            auditLogger: auditLogger);

        var response = await service.SetStrategyStateAsync(
            "strategy-main",
            request,
            CancellationToken.None);

        Assert.Equal(expectedStatus, response.Status);
        var audit = Assert.Single(auditLogger.Entries);
        Assert.Equal("control-room strategy state", audit.CommandName);
        using var payload = JsonDocument.Parse(audit.ArgumentsJson);
        Assert.Equal(expectedStatus, payload.RootElement.GetProperty("outcome").GetString());
        Assert.Equal("strategy-main", payload.RootElement.GetProperty("strategyId").GetString());
        Assert.Equal("control-room-api", payload.RootElement.GetProperty("source").GetString());
        Assert.Equal(request.Actor ?? Environment.UserName, audit.Actor);
    }

    [Fact]
    public async Task SetKillSwitchReturnsDisabledWhenCommandModeIsReadOnly()
    {
        var riskManager = new FakeRiskManager();
        var service = CreateService(
            new ControlRoomOptions
            {
                EnableControlCommands = true,
                CommandMode = ControlRoomCommandModes.ReadOnly
            },
            riskManager: riskManager);

        var response = await service.SetKillSwitchAsync(
            new SetKillSwitchRequest(true, "HardStop", "test", "test"),
            CancellationToken.None);

        Assert.Equal("Disabled", response.Status);
        Assert.Equal(ControlRoomCommandModes.ReadOnly, response.CommandMode);
        Assert.Equal(0, riskManager.ActivateKillSwitchCallCount);
        Assert.Equal(0, riskManager.ResetKillSwitchCallCount);
    }

    [Fact]
    public async Task SetKillSwitchCallsRiskManagerWhenPaperCommandsAreEnabled()
    {
        var riskManager = new FakeRiskManager();
        var service = CreateService(
            new ControlRoomOptions
            {
                EnableControlCommands = true,
                CommandMode = ControlRoomCommandModes.Paper
            },
            riskManager: riskManager);

        var response = await service.SetKillSwitchAsync(
            new SetKillSwitchRequest(true, "SoftStop", "operator-test", "testing"),
            CancellationToken.None);

        Assert.Equal("Accepted", response.Status);
        Assert.Equal(ControlRoomCommandModes.Paper, response.CommandMode);
        Assert.Equal(1, riskManager.ActivateKillSwitchCallCount);
        Assert.Equal(KillSwitchLevel.SoftStop, riskManager.LastKillSwitchLevel);
        Assert.Equal(0, riskManager.ResetKillSwitchCallCount);
    }

    [Fact]
    public async Task SetKillSwitchRequiresConfirmationForHardStop()
    {
        var riskManager = new FakeRiskManager();
        var auditLogger = new FakeCommandAuditLogger();
        var service = CreateService(
            new ControlRoomOptions
            {
                EnableControlCommands = true,
                CommandMode = ControlRoomCommandModes.Paper
            },
            riskManager: riskManager,
            auditLogger: auditLogger);

        var response = await service.SetKillSwitchAsync(
            new SetKillSwitchRequest(true, "HardStop", "operator-test", "testing", Actor: "operator"),
            CancellationToken.None);

        Assert.Equal("ConfirmationRequired", response.Status);
        Assert.Equal(0, riskManager.ActivateKillSwitchCallCount);
        var audit = Assert.Single(auditLogger.Entries);
        Assert.False(audit.Success);
        Assert.Equal(3, audit.ExitCode);
        using var payload = JsonDocument.Parse(audit.ArgumentsJson);
        Assert.True(payload.RootElement.GetProperty("confirmationRequired").GetBoolean());
        Assert.Equal("HardStop", payload.RootElement.GetProperty("level").GetString());
    }

    [Fact]
    public async Task SetKillSwitchRequiresConfirmationForReset()
    {
        var riskManager = new FakeRiskManager();
        var auditLogger = new FakeCommandAuditLogger();
        var service = CreateService(
            new ControlRoomOptions
            {
                EnableControlCommands = true,
                CommandMode = ControlRoomCommandModes.Paper
            },
            riskManager: riskManager,
            auditLogger: auditLogger);

        var response = await service.SetKillSwitchAsync(
            new SetKillSwitchRequest(false, null, "operator-test", "testing", Actor: "operator"),
            CancellationToken.None);

        Assert.Equal("ConfirmationRequired", response.Status);
        Assert.Equal(0, riskManager.ResetKillSwitchCallCount);
        var audit = Assert.Single(auditLogger.Entries);
        Assert.False(audit.Success);
        using var payload = JsonDocument.Parse(audit.ArgumentsJson);
        Assert.True(payload.RootElement.GetProperty("confirmationRequired").GetBoolean());
        Assert.False(payload.RootElement.GetProperty("active").GetBoolean());
    }

    [Fact]
    public async Task SetKillSwitchAuditsAffectedOrdersWhenConfirmedHardStopExecutes()
    {
        var riskManager = new FakeRiskManager
        {
            OpenOrderIds = ["order-1", "order-2"]
        };
        var auditLogger = new FakeCommandAuditLogger();
        var service = CreateService(
            new ControlRoomOptions
            {
                EnableControlCommands = true,
                CommandMode = ControlRoomCommandModes.Paper
            },
            riskManager: riskManager,
            auditLogger: auditLogger);

        var response = await service.SetKillSwitchAsync(
            new SetKillSwitchRequest(
                true,
                "HardStop",
                "operator-test",
                "testing",
                Actor: "operator",
                ConfirmationText: "CONFIRM"),
            CancellationToken.None);

        Assert.Equal("Accepted", response.Status);
        Assert.Equal(1, riskManager.ActivateKillSwitchCallCount);
        var audit = Assert.Single(auditLogger.Entries);
        Assert.True(audit.Success);
        using var payload = JsonDocument.Parse(audit.ArgumentsJson);
        Assert.Equal("Accepted", payload.RootElement.GetProperty("outcome").GetString());
        Assert.Equal("order-1", payload.RootElement.GetProperty("affectedOrders")[0].GetString());
        Assert.Equal("order-2", payload.RootElement.GetProperty("affectedOrders")[1].GetString());
    }

    [Fact]
    public async Task GetIncidentActionsReturnsRequiredActionsWithDisabledReasons()
    {
        var service = CreateService(
            new ControlRoomOptions
            {
                EnableControlCommands = false,
                CommandMode = ControlRoomCommandModes.LiveServices
            },
            queryService: new FakeControlRoomQueryService
            {
                Snapshot = CreateSnapshot(openOrders: 2, killSwitchActive: true, includeStrategy: true)
            });

        var catalog = await service.GetIncidentActionsAsync(CancellationToken.None);

        Assert.Equal(ControlRoomCommandModes.ReadOnly, catalog.CommandMode);
        Assert.Contains("autotrade-incident-runbook.md", catalog.RunbookPath, StringComparison.Ordinal);
        Assert.Contains(catalog.Actions, action => action.Id == "hard-stop");
        Assert.Contains(catalog.Actions, action => action.Id == "reset-kill-switch");
        Assert.Contains(catalog.Actions, action => action.Id == "pause-strategy");
        Assert.Contains(catalog.Actions, action => action.Id == "stop-strategy");
        Assert.Contains(catalog.Actions, action => action.Id == "cancel-open-orders");
        var hardStop = catalog.Actions.Single(action => action.Id == "hard-stop");
        Assert.False(hardStop.Enabled);
        Assert.Contains("disabled", hardStop.DisabledReason, StringComparison.OrdinalIgnoreCase);
        Assert.True(catalog.Actions.Single(action => action.Id == "export-incident-package").Enabled);
    }

    [Fact]
    public async Task CancelOpenOrdersRequiresConfirmationAndAudits()
    {
        var auditLogger = new FakeCommandAuditLogger();
        var orderRepository = new FakeOrderRepository([CreateOrder("client-1")]);
        var executionService = new FakeExecutionService();
        var service = CreateService(
            new ControlRoomOptions
            {
                EnableControlCommands = true,
                CommandMode = ControlRoomCommandModes.Paper
            },
            auditLogger: auditLogger,
            orderRepository: orderRepository,
            executionService: executionService);

        var response = await service.CancelOpenOrdersAsync(
            new CancelOpenOrdersRequest(Actor: "operator", Reason: "incident"),
            CancellationToken.None);

        Assert.Equal("ConfirmationRequired", response.Status);
        Assert.Empty(executionService.CancelledClientOrderIds);
        var audit = Assert.Single(auditLogger.Entries);
        Assert.Equal("control-room cancel open orders", audit.CommandName);
        Assert.False(audit.Success);
        using var payload = JsonDocument.Parse(audit.ArgumentsJson);
        Assert.True(payload.RootElement.GetProperty("confirmationRequired").GetBoolean());
    }

    [Fact]
    public async Task CancelOpenOrdersCancelsFilteredOrdersAndAuditsResults()
    {
        var auditLogger = new FakeCommandAuditLogger();
        var orderRepository = new FakeOrderRepository(
        [
            CreateOrder("client-1", strategyId: "strategy-main"),
            CreateOrder("client-2", strategyId: "other-strategy")
        ]);
        var executionService = new FakeExecutionService();
        var service = CreateService(
            new ControlRoomOptions
            {
                EnableControlCommands = true,
                CommandMode = ControlRoomCommandModes.Paper
            },
            auditLogger: auditLogger,
            orderRepository: orderRepository,
            executionService: executionService);

        var response = await service.CancelOpenOrdersAsync(
            new CancelOpenOrdersRequest(
                Actor: "operator",
                ReasonCode: "INCIDENT",
                Reason: "risk response",
                StrategyId: "strategy-main",
                ConfirmationText: "CONFIRM"),
            CancellationToken.None);

        Assert.Equal("Accepted", response.Status);
        Assert.Equal(["client-1"], executionService.CancelledClientOrderIds);
        var audit = Assert.Single(auditLogger.Entries);
        Assert.True(audit.Success);
        using var payload = JsonDocument.Parse(audit.ArgumentsJson);
        Assert.Equal(1, payload.RootElement.GetProperty("candidateCount").GetInt32());
        Assert.Equal(1, payload.RootElement.GetProperty("acceptedCount").GetInt32());
        Assert.Equal("client-1", payload.RootElement.GetProperty("cancelResults")[0].GetProperty("clientOrderId").GetString());
    }

    [Fact]
    public async Task CancelOpenOrdersReturnsUnsupportedWhenExecutionBoundaryIsUnavailable()
    {
        var orderRepository = new FakeOrderRepository([CreateOrder("client-1")]);
        var auditLogger = new FakeCommandAuditLogger();
        var service = CreateService(
            new ControlRoomOptions
            {
                EnableControlCommands = true,
                CommandMode = ControlRoomCommandModes.Paper
            },
            auditLogger: auditLogger,
            orderRepository: orderRepository);

        var response = await service.CancelOpenOrdersAsync(
            new CancelOpenOrdersRequest(Actor: "operator", ConfirmationText: "CONFIRM"),
            CancellationToken.None);

        Assert.Equal("Unsupported", response.Status);
        var audit = Assert.Single(auditLogger.Entries);
        Assert.False(audit.Success);
        using var payload = JsonDocument.Parse(audit.ArgumentsJson);
        Assert.False(payload.RootElement.GetProperty("executionServiceAvailable").GetBoolean());
    }

    [Fact]
    public async Task ExportIncidentPackageReturnsReadOnlyEvidenceAndAudits()
    {
        var auditLogger = new FakeCommandAuditLogger();
        var service = CreateService(
            new ControlRoomOptions
            {
                EnableControlCommands = false,
                CommandMode = ControlRoomCommandModes.ReadOnly
            },
            auditLogger: auditLogger,
            queryService: new FakeControlRoomQueryService
            {
                Snapshot = CreateSnapshot(openOrders: 1, killSwitchActive: true, includeStrategy: true)
            });

        var package = await service.ExportIncidentPackageAsync(
            new IncidentPackageQuery(RiskEventId: "11111111-1111-1111-1111-111111111111"),
            CancellationToken.None);

        Assert.Equal("control-room-incident-package.v1", package.ContractVersion);
        Assert.True(package.Snapshot.Risk.KillSwitchActive);
        Assert.Contains(package.ExportReferences, reference => reference.Contains("/api/control-room/risk/events/", StringComparison.Ordinal));
        var audit = Assert.Single(auditLogger.Entries);
        Assert.Equal("control-room incident package export", audit.CommandName);
        Assert.True(audit.Success);
        using var payload = JsonDocument.Parse(audit.ArgumentsJson);
        Assert.True(payload.RootElement.GetProperty("readOnly").GetBoolean());
        Assert.Equal("Accepted", payload.RootElement.GetProperty("outcome").GetString());
    }

    [Fact]
    public async Task ArmLiveRequiresLiveServicesCommandMode()
    {
        var liveArmingService = new FakeLiveArmingService();
        var service = CreateService(
            new ControlRoomOptions
            {
                EnableControlCommands = true,
                CommandMode = ControlRoomCommandModes.Paper
            },
            liveArmingService: liveArmingService);

        var response = await service.ArmLiveAsync(
            new ArmLiveRequest(Actor: "operator", ConfirmationText: "ARM LIVE"),
            CancellationToken.None);

        Assert.Equal("Disabled", response.Status);
        Assert.Equal(0, liveArmingService.ArmCallCount);
    }

    [Fact]
    public async Task ArmLiveCallsServiceAndAuditsAcceptedEvidenceWhenLiveServicesEnabled()
    {
        var liveArmingService = new FakeLiveArmingService
        {
            ArmResult = FakeLiveArmingService.AcceptedResult()
        };
        var auditLogger = new FakeCommandAuditLogger();
        var service = CreateService(
            new ControlRoomOptions
            {
                EnableControlCommands = true,
                CommandMode = ControlRoomCommandModes.LiveServices
            },
            auditLogger: auditLogger,
            liveArmingService: liveArmingService);

        var response = await service.ArmLiveAsync(
            new ArmLiveRequest(Actor: "operator", Reason: "regression", ConfirmationText: "ARM LIVE"),
            CancellationToken.None);

        Assert.Equal("Accepted", response.Status);
        Assert.Equal(1, liveArmingService.ArmCallCount);
        Assert.Equal("operator", liveArmingService.LastArmRequest?.Actor);
        var audit = Assert.Single(auditLogger.Entries);
        Assert.True(audit.Success);
        using var payload = JsonDocument.Parse(audit.ArgumentsJson);
        Assert.Equal("arm", payload.RootElement.GetProperty("action").GetString());
        Assert.Equal("evidence-1", payload.RootElement.GetProperty("evidenceId").GetString());
        Assert.True(payload.RootElement.GetProperty("confirmationProvided").GetBoolean());
    }

    [Fact]
    public async Task DisarmLiveCallsServiceEvenWhenCommandModeIsReadOnly()
    {
        var liveArmingService = new FakeLiveArmingService
        {
            DisarmResult = FakeLiveArmingService.DisarmedResult()
        };
        var auditLogger = new FakeCommandAuditLogger();
        var service = CreateService(
            new ControlRoomOptions
            {
                EnableControlCommands = false,
                CommandMode = ControlRoomCommandModes.ReadOnly
            },
            auditLogger: auditLogger,
            liveArmingService: liveArmingService);

        var response = await service.DisarmLiveAsync(
            new DisarmLiveRequest(Actor: "operator", Reason: "risk-off", ConfirmationText: "DISARM LIVE"),
            CancellationToken.None);

        Assert.Equal("Accepted", response.Status);
        Assert.Equal(1, liveArmingService.DisarmCallCount);
        Assert.Equal("operator", liveArmingService.LastDisarmRequest?.Actor);
        var audit = Assert.Single(auditLogger.Entries);
        Assert.True(audit.Success);
        using var payload = JsonDocument.Parse(audit.ArgumentsJson);
        Assert.Equal("disarm", payload.RootElement.GetProperty("action").GetString());
        Assert.Equal(ControlRoomCommandModes.ReadOnly, payload.RootElement.GetProperty("commandMode").GetString());
    }

    [Fact]
    public void ControlRoomOptionsDefaultToLocalReadOnlyAccess()
    {
        var options = new ControlRoomOptions();

        Assert.False(options.EnableControlCommands);
        Assert.True(options.RequireLocalAccess);
        Assert.Equal(ControlRoomCommandModes.ReadOnly, options.CommandMode);
        Assert.Equal(ControlRoomCommandModes.ReadOnly, options.EffectiveCommandMode);
        Assert.False(options.AllowsControlCommands);
    }

    private static ControlRoomCommandService CreateService(
        ControlRoomOptions options,
        IStrategyManager? strategyManager = null,
        IRiskManager? riskManager = null,
        ICommandAuditLogger? auditLogger = null,
        ILiveArmingService? liveArmingService = null,
        IArcAccessDecisionService? accessDecisionService = null,
        IOrderRepository? orderRepository = null,
        IExecutionService? executionService = null,
        IControlRoomQueryService? queryService = null)
    {
        var services = new ServiceCollection();
        if (strategyManager is not null)
        {
            services.AddSingleton(strategyManager);
        }

        if (riskManager is not null)
        {
            services.AddSingleton(riskManager);
        }

        if (auditLogger is not null)
        {
            services.AddSingleton(auditLogger);
        }

        if (liveArmingService is not null)
        {
            services.AddSingleton(liveArmingService);
        }

        if (accessDecisionService is not null)
        {
            services.AddSingleton(accessDecisionService);
        }

        if (orderRepository is not null)
        {
            services.AddSingleton(orderRepository);
        }

        if (executionService is not null)
        {
            services.AddSingleton(executionService);
        }

        return new ControlRoomCommandService(
            services.BuildServiceProvider(),
            queryService ?? new FakeControlRoomQueryService(),
            new TestOptionsMonitor<ControlRoomOptions>(options));
    }

    private const string Wallet = "0x1234567890abcdef1234567890abcdef12345678";

    private static ArcAccessDecision CreateArcDecision(bool allowed, string reasonCode)
        => new(
            allowed,
            reasonCode,
            allowed
                ? "Access allowed by active Arc subscription entitlement."
                : "No active Arc subscription entitlement was found for this wallet and strategy.",
            ArcEntitlementPermission.RequestPaperAutoTrade,
            "strategy-main",
            Wallet,
            "arc-paper-autotrade",
            "strategy-main",
            Tier: allowed ? "PaperAutotrade" : null,
            ExpiresAtUtc: allowed ? new DateTimeOffset(2026, 5, 10, 8, 30, 0, TimeSpan.Zero) : null,
            EvidenceTransactionHash: allowed ? "0xaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" : null);

    private static ControlRoomSnapshotResponse CreateSnapshot(
        int openOrders,
        bool killSwitchActive,
        bool includeStrategy)
    {
        var now = new DateTimeOffset(2026, 5, 3, 8, 30, 0, TimeSpan.Zero);
        return new ControlRoomSnapshotResponse(
            TimestampUtc: now,
            DataMode: "test",
            CommandMode: ControlRoomCommandModes.ReadOnly,
            Process: new ControlRoomProcessDto("Ready", "Test", "Paper", true, 1, 0, 0),
            Risk: new ControlRoomRiskDto(
                killSwitchActive,
                killSwitchActive ? "HardStop" : "None",
                killSwitchActive ? "INCIDENT" : null,
                killSwitchActive ? now : null,
                100m,
                90m,
                10m,
                openOrders * 10m,
                openOrders,
                0,
                []),
            Metrics: [],
            Strategies: includeStrategy
                ?
                [
                    new ControlRoomStrategyDto(
                        "strategy-main",
                        "Strategy Main",
                        StrategyState.Running,
                        true,
                        "v1",
                        "Running",
                        1,
                        1,
                        1,
                        0,
                        killSwitchActive,
                        now,
                        now,
                        null,
                        null,
                        [])
                ]
                : [],
            Markets: [],
            Orders: Enumerable.Range(1, openOrders)
                .Select(index => new ControlRoomOrderDto(
                    $"client-{index}",
                    "strategy-main",
                    "market-main",
                    "Buy",
                    "YES",
                    0.5m,
                    10m,
                    0m,
                    "Open",
                    now))
                .ToArray(),
            Positions: [],
            Decisions: [],
            Timeline: [],
            CapitalCurve: [],
            LatencyCurve: []);
    }

    private static OrderDto CreateOrder(
        string clientOrderId,
        string strategyId = "strategy-main",
        string marketId = "market-main")
    {
        var now = new DateTimeOffset(2026, 5, 3, 8, 30, 0, TimeSpan.Zero);
        return new OrderDto(
            Id: OrderAuditIds.ForClientOrderId(clientOrderId),
            TradingAccountId: Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            MarketId: marketId,
            TokenId: "token-yes",
            StrategyId: strategyId,
            ClientOrderId: clientOrderId,
            ExchangeOrderId: $"exchange-{clientOrderId}",
            CorrelationId: "correlation-1",
            Outcome: OutcomeSide.Yes,
            Side: OrderSide.Buy,
            OrderType: OrderType.Limit,
            TimeInForce: TimeInForce.Gtc,
            GoodTilDateUtc: null,
            NegRisk: false,
            Price: 0.5m,
            Quantity: 10m,
            FilledQuantity: 0m,
            Status: OrderStatus.Open,
            RejectionReason: null,
            CreatedAtUtc: now,
            UpdatedAtUtc: now);
    }

    public static TheoryData<string, ControlRoomOptions, SetStrategyStateRequest, Exception?> StrategyStateAuditCases()
    {
        return new TheoryData<string, ControlRoomOptions, SetStrategyStateRequest, Exception?>
        {
            {
                "Disabled",
                new ControlRoomOptions
                {
                    EnableControlCommands = false,
                    CommandMode = ControlRoomCommandModes.LiveServices
                },
                new SetStrategyStateRequest("Paused", Actor: "operator"),
                null
            },
            {
                "InvalidRequest",
                new ControlRoomOptions
                {
                    EnableControlCommands = true,
                    CommandMode = ControlRoomCommandModes.Paper
                },
                new SetStrategyStateRequest("Created", Actor: "operator"),
                null
            },
            {
                "ConfirmationRequired",
                new ControlRoomOptions
                {
                    EnableControlCommands = true,
                    CommandMode = ControlRoomCommandModes.LiveServices
                },
                new SetStrategyStateRequest("Running", Actor: "operator"),
                null
            },
            {
                "Accepted",
                new ControlRoomOptions
                {
                    EnableControlCommands = true,
                    CommandMode = ControlRoomCommandModes.Paper
                },
                new SetStrategyStateRequest("Paused", Actor: "operator"),
                null
            },
            {
                "Rejected",
                new ControlRoomOptions
                {
                    EnableControlCommands = true,
                    CommandMode = ControlRoomCommandModes.Paper
                },
                new SetStrategyStateRequest("Paused", Actor: "operator"),
                new InvalidOperationException("manager rejected command")
            }
        };
    }

    private sealed class FakeControlRoomQueryService : IControlRoomQueryService
    {
        public Exception? SnapshotException { get; init; }

        public ControlRoomSnapshotResponse Snapshot { get; init; } = CreateSnapshot(
            openOrders: 0,
            killSwitchActive: false,
            includeStrategy: false);

        public Task<ControlRoomSnapshotResponse> GetSnapshotAsync(CancellationToken cancellationToken = default)
        {
            if (SnapshotException is not null)
            {
                throw SnapshotException;
            }

            return Task.FromResult(Snapshot);
        }
    }

    private sealed class FakeStrategyManager : IStrategyManager
    {
        public Exception? SetDesiredStateException { get; init; }

        public int SetDesiredStateCallCount { get; private set; }

        public (string StrategyId, StrategyState State)? LastDesiredState { get; private set; }

        public IReadOnlyList<StrategyDescriptor> GetRegisteredStrategies()
        {
            return [];
        }

        public Task<IReadOnlyList<StrategyStatus>> GetStatusesAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<StrategyStatus>>([]);
        }

        public StrategyState GetDesiredState(string strategyId)
        {
            return StrategyState.Running;
        }

        public Task SetDesiredStateAsync(
            string strategyId,
            StrategyState desiredState,
            CancellationToken cancellationToken = default)
        {
            if (SetDesiredStateException is not null)
            {
                throw SetDesiredStateException;
            }

            SetDesiredStateCallCount++;
            LastDesiredState = (strategyId, desiredState);
            return Task.CompletedTask;
        }

        public Task StartAsync(string strategyId, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task PauseAsync(string strategyId, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task ResumeAsync(string strategyId, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task StopAsync(string strategyId, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task ReloadConfigAsync(string strategyId, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class FakeRiskManager : IRiskManager
    {
        public IReadOnlyList<string> OpenOrderIds { get; init; } = [];

        public int ActivateKillSwitchCallCount { get; private set; }

        public int ResetKillSwitchCallCount { get; private set; }

        public KillSwitchLevel? LastKillSwitchLevel { get; private set; }

        public bool IsKillSwitchActive => ActivateKillSwitchCallCount > ResetKillSwitchCallCount;

        public Task<RiskCheckResult> ValidateOrderAsync(RiskOrderRequest request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task RecordOrderAcceptedAsync(RiskOrderRequest request, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task RecordOrderUpdateAsync(RiskOrderUpdate update, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task RecordOrderErrorAsync(
            string strategyId,
            string clientOrderId,
            string errorCode,
            string message,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task ActivateKillSwitchAsync(string reason, CancellationToken cancellationToken = default)
        {
            ActivateKillSwitchCallCount++;
            LastKillSwitchLevel = KillSwitchLevel.HardStop;
            return Task.CompletedTask;
        }

        public Task ActivateKillSwitchAsync(
            KillSwitchLevel level,
            string reasonCode,
            string reason,
            string? contextJson = null,
            CancellationToken cancellationToken = default)
        {
            ActivateKillSwitchCallCount++;
            LastKillSwitchLevel = level;
            return Task.CompletedTask;
        }

        public Task ActivateStrategyKillSwitchAsync(
            string strategyId,
            KillSwitchLevel level,
            string reasonCode,
            string reason,
            string? marketId = null,
            string? contextJson = null,
            CancellationToken cancellationToken = default)
        {
            ActivateKillSwitchCallCount++;
            LastKillSwitchLevel = level;
            return Task.CompletedTask;
        }

        public Task ResetKillSwitchAsync(string? strategyId = null, CancellationToken cancellationToken = default)
        {
            ResetKillSwitchCallCount++;
            return Task.CompletedTask;
        }

        public KillSwitchState GetKillSwitchState()
        {
            throw new NotSupportedException();
        }

        public KillSwitchState GetStrategyKillSwitchState(string strategyId)
        {
            throw new NotSupportedException();
        }

        public bool IsStrategyBlocked(string strategyId)
        {
            return false;
        }

        public IReadOnlyList<KillSwitchState> GetAllActiveKillSwitches()
        {
            return [];
        }

        public IReadOnlyList<string> GetOpenOrderIds()
        {
            return OpenOrderIds;
        }

        public IReadOnlyList<string> GetOpenOrderIds(string strategyId)
        {
            return [];
        }

        public IReadOnlyList<UnhedgedExposureSnapshot> GetExpiredUnhedgedExposures(DateTimeOffset nowUtc)
        {
            return [];
        }

        public Task RecordUnhedgedExposureAsync(
            string strategyId,
            string marketId,
            string tokenId,
            string hedgeTokenId,
            OutcomeSide outcome,
            OrderSide side,
            decimal quantity,
            decimal price,
            DateTimeOffset startedAtUtc,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task ClearUnhedgedExposureAsync(string strategyId, string marketId, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public RiskStateSnapshot GetStateSnapshot()
        {
            throw new NotSupportedException();
        }
    }

    private sealed class FakeOrderRepository(IReadOnlyList<OrderDto> openOrders) : IOrderRepository
    {
        public Task AddAsync(OrderDto order, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task AddRangeAsync(IEnumerable<OrderDto> orders, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task UpdateAsync(OrderDto order, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<OrderDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<OrderDto?> GetByClientOrderIdAsync(string clientOrderId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<OrderDto?> GetByExchangeOrderIdAsync(string exchangeOrderId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<OrderDto>> GetOpenOrdersAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(openOrders);

        public Task<IReadOnlyList<OrderDto>> GetByStrategyIdAsync(
            string strategyId,
            DateTimeOffset? from = null,
            DateTimeOffset? to = null,
            int? limit = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<OrderDto>> GetByMarketIdAsync(
            string marketId,
            DateTimeOffset? from = null,
            DateTimeOffset? to = null,
            int? limit = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<OrderDto>> GetByStatusAsync(
            OrderStatus status,
            DateTimeOffset? from = null,
            DateTimeOffset? to = null,
            int? limit = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<PagedResultDto<OrderDto>> GetPagedAsync(
            int page,
            int pageSize,
            string? strategyId = null,
            string? marketId = null,
            OrderStatus? status = null,
            DateTimeOffset? from = null,
            DateTimeOffset? to = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<int> DeleteBeforeAsync(DateTimeOffset beforeUtc, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class FakeExecutionService : IExecutionService
    {
        public List<string> CancelledClientOrderIds { get; } = [];

        public Task<ExecutionResult> PlaceOrderAsync(
            ExecutionRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<ExecutionResult>> PlaceOrdersAsync(
            IReadOnlyList<ExecutionRequest> requests,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<ExecutionResult> CancelOrderAsync(
            string clientOrderId,
            CancellationToken cancellationToken = default)
        {
            CancelledClientOrderIds.Add(clientOrderId);
            return Task.FromResult(ExecutionResult.Succeed(
                clientOrderId,
                $"exchange-{clientOrderId}",
                ExecutionStatus.Cancelled));
        }

        public Task<OrderStatusResult> GetOrderStatusAsync(
            string clientOrderId,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class FakeLiveArmingService : ILiveArmingService
    {
        public LiveArmingResult ArmResult { get; init; } = new(
            false,
            "ConfirmationRequired",
            "Type ARM LIVE to arm Live trading.",
            NotArmedStatus());

        public LiveArmingResult DisarmResult { get; init; } = new(
            false,
            "ConfirmationRequired",
            "Type DISARM LIVE to disarm Live trading.",
            NotArmedStatus());

        public int ArmCallCount { get; private set; }

        public int DisarmCallCount { get; private set; }

        public LiveArmingRequest? LastArmRequest { get; private set; }

        public LiveDisarmingRequest? LastDisarmRequest { get; private set; }

        public Task<LiveArmingStatus> GetStatusAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(NotArmedStatus());
        }

        public Task<LiveArmingResult> ArmAsync(
            LiveArmingRequest request,
            CancellationToken cancellationToken = default)
        {
            ArmCallCount++;
            LastArmRequest = request;
            return Task.FromResult(ArmResult);
        }

        public Task<LiveArmingResult> DisarmAsync(
            LiveDisarmingRequest request,
            CancellationToken cancellationToken = default)
        {
            DisarmCallCount++;
            LastDisarmRequest = request;
            return Task.FromResult(DisarmResult);
        }

        public Task<LiveArmingStatus> RequireArmedAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(NotArmedStatus());
        }

        public static LiveArmingResult AcceptedResult()
        {
            var status = ArmedStatus();
            return new LiveArmingResult(true, "Accepted", "Live trading armed.", status);
        }

        public static LiveArmingResult DisarmedResult()
        {
            var status = NotArmedStatus();
            return new LiveArmingResult(true, "Accepted", "Live trading disarmed.", status);
        }

        private static LiveArmingStatus ArmedStatus()
        {
            var now = new DateTimeOffset(2026, 5, 3, 8, 30, 0, TimeSpan.Zero);
            var evidence = new LiveArmingEvidence(
                "evidence-1",
                "operator",
                "regression",
                now.AddMinutes(-1),
                now.AddHours(4),
                "test",
                "fingerprint",
                new LiveArmingRiskSummary(100m, 80m, 20m, 10m, 1, 0, false),
                ["risk.limits.configured"]);

            return new LiveArmingStatus(
                true,
                "Armed",
                "Live trading is armed.",
                "test",
                now,
                evidence,
                []);
        }

        private static LiveArmingStatus NotArmedStatus()
        {
            var now = new DateTimeOffset(2026, 5, 3, 8, 30, 0, TimeSpan.Zero);
            const string reason = "Live arming evidence has not been recorded.";
            return new LiveArmingStatus(
                false,
                "NotArmed",
                reason,
                "test",
                now,
                null,
                [reason]);
        }
    }

    private sealed class FakeArcAccessDecisionService(
        ArcAccessDecision decision) : IArcAccessDecisionService
    {
        public ArcAccessDecisionRequest? LastRequest { get; private set; }

        public Task<ArcAccessDecision> EvaluateAsync(
            ArcAccessDecisionRequest request,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(decision);
        }
    }

    private sealed class FakeCommandAuditLogger : ICommandAuditLogger
    {
        public List<CommandAuditEntry> Entries { get; } = [];

        public Task LogAsync(CommandAuditEntry entry, CancellationToken cancellationToken = default)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }
    }
}
