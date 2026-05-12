using System.Diagnostics;
using System.Text.Json;
using Autotrade.ArcSettlement.Application.Contract.Access;
using Autotrade.Strategy.Application.Audit;
using Autotrade.Strategy.Application.Contract.Strategies;
using Autotrade.Strategy.Application.Engine;
using Autotrade.Trading.Application.Contract.Execution;
using Autotrade.Trading.Application.Contract.Repositories;
using Autotrade.Trading.Application.Contract.Risk;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Autotrade.Api.ControlRoom;

public sealed class ControlRoomCommandService(
    IServiceProvider serviceProvider,
    IControlRoomQueryService queryService,
    IOptionsMonitor<ControlRoomOptions> options) : IControlRoomCommandService
{
    private const string ConfirmationText = "CONFIRM";
    private const string IncidentRunbookPath = "docs/operations/autotrade-incident-runbook.md";
    private const string IncidentPackageContractVersion = "control-room-incident-package.v1";

    private static readonly HashSet<StrategyState> AllowedTargets =
    [
        StrategyState.Running,
        StrategyState.Paused,
        StrategyState.Stopped
    ];

    public async Task<ControlRoomCommandResponse> SetStrategyStateAsync(
        string strategyId,
        SetStrategyStateRequest request,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        ArgumentException.ThrowIfNullOrWhiteSpace(strategyId);
        ArgumentNullException.ThrowIfNull(request);
        var actor = ResolveActor(request.Actor);
        var commandMode = options.CurrentValue.EffectiveCommandMode;
        var audit = BuildStrategyStateAudit(strategyId, request, actor, commandMode);

        if (!options.CurrentValue.AllowsControlCommands)
        {
            return await BuildCommandResponseAsync(
                "control-room strategy state",
                audit,
                actor,
                "Disabled",
                "Control room commands are disabled by configuration or command mode.",
                success: false,
                exitCode: 2,
                stopwatch,
                cancellationToken).ConfigureAwait(false);
        }

        if (!Enum.TryParse<StrategyState>(request.TargetState, ignoreCase: true, out var state)
            || !AllowedTargets.Contains(state))
        {
            return await BuildCommandResponseAsync(
                "control-room strategy state",
                audit,
                actor,
                "InvalidRequest",
                "TargetState must be Running, Paused, or Stopped.",
                success: false,
                exitCode: 2,
                stopwatch,
                cancellationToken).ConfigureAwait(false);
        }

        audit["targetState"] = state.ToString();

        if (RequiresStrategyConfirmation(state, commandMode)
            && !HasConfirmation(request.ConfirmationText))
        {
            audit["confirmationRequired"] = true;
            return await BuildCommandResponseAsync(
                "control-room strategy state",
                audit,
                actor,
                "ConfirmationRequired",
                "Type CONFIRM before enabling a live strategy from the control room.",
                success: false,
                exitCode: 3,
                stopwatch,
                cancellationToken).ConfigureAwait(false);
        }

        try
        {
            var manager = serviceProvider.GetService<IStrategyManager>()
                ?? throw new InvalidOperationException("Strategy manager is unavailable. Enable AutotradeApi:EnableModules to control live strategies.");

            var previousState = manager.GetDesiredState(strategyId);
            audit["previousState"] = previousState.ToString();

            await manager.SetDesiredStateAsync(strategyId, state, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            audit["error"] = exception.Message;
            return await BuildCommandResponseAsync(
                "control-room strategy state",
                audit,
                actor,
                "Rejected",
                exception.Message,
                success: false,
                exitCode: 1,
                stopwatch,
                cancellationToken).ConfigureAwait(false);
        }

        return await BuildCommandResponseAsync(
            "control-room strategy state",
            audit,
            actor,
            "Accepted",
            $"Strategy {strategyId} target state set to {state}.",
            success: true,
            exitCode: 0,
            stopwatch,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<ArcPaperAutoTradeResponse> RequestArcPaperAutoTradeAsync(
        string strategyId,
        ArcPaperAutoTradeRequest request,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        ArgumentException.ThrowIfNullOrWhiteSpace(strategyId);
        ArgumentNullException.ThrowIfNull(request);
        var actor = ResolveActor(request.Actor);
        var commandMode = options.CurrentValue.EffectiveCommandMode;
        var audit = BuildArcPaperAutoTradeAudit(strategyId, request, actor, commandMode);

        var accessDecisionService = serviceProvider.GetService<IArcAccessDecisionService>();
        if (accessDecisionService is null)
        {
            return await BuildArcPaperAutoTradeResponseAsync(
                audit,
                actor,
                "Unsupported",
                "Arc access decision service is unavailable. Enable AutotradeApi:EnableModules and ArcSettlement services.",
                CreateFallbackDecision(strategyId, request.WalletAddress, "ARC_ACCESS_UNAVAILABLE"),
                command: null,
                success: false,
                exitCode: 2,
                stopwatch,
                cancellationToken).ConfigureAwait(false);
        }

        var decision = await accessDecisionService
            .EvaluateAsync(
                new ArcAccessDecisionRequest(
                    request.WalletAddress,
                    strategyId,
                    ArcEntitlementPermission.RequestPaperAutoTrade,
                    "arc-paper-autotrade",
                    strategyId),
                cancellationToken)
            .ConfigureAwait(false);
        AddAccessDecisionAudit(audit, decision);

        if (!decision.Allowed)
        {
            return await BuildArcPaperAutoTradeResponseAsync(
                audit,
                actor,
                "AccessDenied",
                decision.Reason,
                decision,
                command: null,
                success: false,
                exitCode: 2,
                stopwatch,
                cancellationToken).ConfigureAwait(false);
        }

        if (!options.CurrentValue.AllowsControlCommands)
        {
            return await BuildArcPaperAutoTradeResponseAsync(
                audit,
                actor,
                "Disabled",
                "Control room commands are disabled by configuration or command mode.",
                decision,
                command: null,
                success: false,
                exitCode: 2,
                stopwatch,
                cancellationToken).ConfigureAwait(false);
        }

        if (string.Equals(commandMode, ControlRoomCommandModes.LiveServices, StringComparison.Ordinal))
        {
            return await BuildArcPaperAutoTradeResponseAsync(
                audit,
                actor,
                "LiveTradingBlocked",
                "Arc paper auto-trade permission cannot start a LiveServices command path.",
                decision,
                command: null,
                success: false,
                exitCode: 2,
                stopwatch,
                cancellationToken).ConfigureAwait(false);
        }

        var snapshot = await queryService.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        var strategyExists = snapshot.Strategies.Any(
            strategy => string.Equals(strategy.StrategyId, strategyId, StringComparison.OrdinalIgnoreCase));
        audit["strategyExists"] = strategyExists;
        if (!strategyExists)
        {
            return await BuildArcPaperAutoTradeResponseAsync(
                audit,
                actor,
                "StrategyNotFound",
                $"Strategy '{strategyId}' was not found in the control-room snapshot.",
                decision,
                command: null,
                success: false,
                exitCode: 2,
                stopwatch,
                cancellationToken).ConfigureAwait(false);
        }

        var command = await SetStrategyStateAsync(
                strategyId,
                new SetStrategyStateRequest(
                    "Running",
                    actor,
                    "ARC_PAPER_AUTOTRADE",
                    string.IsNullOrWhiteSpace(request.Reason)
                        ? "Arc subscription authorized paper auto-trade request."
                        : request.Reason,
                    request.ConfirmationText),
                cancellationToken)
            .ConfigureAwait(false);
        audit["stateCommandStatus"] = command.Status;
        audit["stateCommandMessage"] = command.Message;

        return await BuildArcPaperAutoTradeResponseAsync(
            audit,
            actor,
            command.Status,
            command.Message,
            decision,
            command,
            success: string.Equals(command.Status, "Accepted", StringComparison.Ordinal),
            exitCode: string.Equals(command.Status, "Accepted", StringComparison.Ordinal) ? 0 : 1,
            stopwatch,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<ControlRoomCommandResponse> SetKillSwitchAsync(
        SetKillSwitchRequest request,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        ArgumentNullException.ThrowIfNull(request);
        var actor = ResolveActor(request.Actor);
        var commandMode = options.CurrentValue.EffectiveCommandMode;
        var level = NormalizeKillSwitchLevel(request.Active, request.Level);
        var audit = BuildKillSwitchAudit(request, actor, commandMode, level);

        if (!options.CurrentValue.AllowsControlCommands)
        {
            return await BuildCommandResponseAsync(
                "control-room kill switch",
                audit,
                actor,
                "Disabled",
                "Control room commands are disabled by configuration or command mode.",
                success: false,
                exitCode: 2,
                stopwatch,
                cancellationToken).ConfigureAwait(false);
        }

        if (RequiresKillSwitchConfirmation(request.Active, level)
            && !HasConfirmation(request.ConfirmationText))
        {
            audit["confirmationRequired"] = true;
            return await BuildCommandResponseAsync(
                "control-room kill switch",
                audit,
                actor,
                "ConfirmationRequired",
                "Type CONFIRM before resetting the kill switch or issuing a hard stop.",
                success: false,
                exitCode: 3,
                stopwatch,
                cancellationToken).ConfigureAwait(false);
        }

        string message;
        try
        {
            var riskManager = serviceProvider.GetService<IRiskManager>()
                ?? throw new InvalidOperationException("Risk manager is unavailable. Enable AutotradeApi:EnableModules to control the live kill switch.");

            audit["affectedOrders"] = riskManager.GetOpenOrderIds();

            if (request.Active)
            {
                await riskManager.ActivateKillSwitchAsync(
                    level,
                    string.IsNullOrWhiteSpace(request.ReasonCode) ? "UI_CONTROL" : request.ReasonCode,
                    string.IsNullOrWhiteSpace(request.Reason) ? "Control room kill switch" : request.Reason,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await riskManager.ResetKillSwitchAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            }

            message = request.Active
                ? $"Kill switch activated at {level}."
                : "Kill switch reset.";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            audit["error"] = exception.Message;
            return await BuildCommandResponseAsync(
                "control-room kill switch",
                audit,
                actor,
                "Rejected",
                exception.Message,
                success: false,
                exitCode: 1,
                stopwatch,
                cancellationToken).ConfigureAwait(false);
        }

        return await BuildCommandResponseAsync(
            "control-room kill switch",
            audit,
            actor,
            "Accepted",
            message,
            success: true,
            exitCode: 0,
            stopwatch,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<IncidentActionCatalog> GetIncidentActionsAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = await queryService.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        var commandMode = options.CurrentValue.EffectiveCommandMode;
        var commandsDisabledReason = options.CurrentValue.AllowsControlCommands
            ? null
            : "Control room commands are disabled by configuration or command mode.";
        var hasOpenOrders = snapshot.Risk.OpenOrders > 0 || snapshot.Orders.Count > 0;
        var hasStrategies = snapshot.Strategies.Count > 0;
        var hasExecutionService = IsServiceAvailable<IExecutionService>();
        var hasOrderRepository = IsServiceAvailable<IOrderRepository>();

        return new IncidentActionCatalog(
            DateTimeOffset.UtcNow,
            commandMode,
            IncidentRunbookPath,
            [
                new IncidentActionDescriptor(
                    "hard-stop",
                    "Hard stop",
                    "Risk",
                    "Global",
                    "POST",
                    "/api/control-room/risk/kill-switch",
                    commandsDisabledReason is null && !snapshot.Risk.KillSwitchActive,
                    commandsDisabledReason ?? (snapshot.Risk.KillSwitchActive ? "Kill switch is already active." : null),
                    ConfirmationText,
                    "Activates the global kill switch; it does not promise exchange-side cancellation."),
                new IncidentActionDescriptor(
                    "reset-kill-switch",
                    "Reset kill switch",
                    "Risk",
                    "Global",
                    "POST",
                    "/api/control-room/risk/kill-switch",
                    commandsDisabledReason is null && snapshot.Risk.KillSwitchActive,
                    commandsDisabledReason ?? (snapshot.Risk.KillSwitchActive ? null : "Kill switch is not active."),
                    ConfirmationText,
                    "Resets the global kill switch after operator verification."),
                new IncidentActionDescriptor(
                    "pause-strategy",
                    "Pause strategy",
                    "Strategy",
                    "Strategy",
                    "POST",
                    "/api/control-room/strategies/{strategyId}/state",
                    commandsDisabledReason is null && hasStrategies,
                    commandsDisabledReason ?? (hasStrategies ? null : "No strategy is registered."),
                    null,
                    "Moves the selected strategy desired state to Paused."),
                new IncidentActionDescriptor(
                    "stop-strategy",
                    "Stop strategy",
                    "Strategy",
                    "Strategy",
                    "POST",
                    "/api/control-room/strategies/{strategyId}/state",
                    commandsDisabledReason is null && hasStrategies,
                    commandsDisabledReason ?? (hasStrategies ? null : "No strategy is registered."),
                    null,
                    "Moves the selected strategy desired state to Stopped."),
                new IncidentActionDescriptor(
                    "cancel-open-orders",
                    "Cancel open orders",
                    "Execution",
                    "Global or filtered",
                    "POST",
                    "/api/control-room/incidents/cancel-open-orders",
                    commandsDisabledReason is null && hasOpenOrders && hasExecutionService && hasOrderRepository,
                    commandsDisabledReason
                        ?? ResolveCancelOpenOrdersDisabledReason(hasOpenOrders, hasExecutionService, hasOrderRepository),
                    ConfirmationText,
                    "Attempts exchange or paper cancellation for open orders that still have client order ids."),
                new IncidentActionDescriptor(
                    "export-incident-package",
                    "Export incident package",
                    "Evidence",
                    "Read-only",
                    "GET",
                    "/api/control-room/incidents/package",
                    true,
                    null,
                    null,
                    "Exports current control-room evidence and action state for offline incident review.")
            ]);
    }

    public async Task<ControlRoomCommandResponse> CancelOpenOrdersAsync(
        CancelOpenOrdersRequest request,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        ArgumentNullException.ThrowIfNull(request);
        var actor = ResolveActor(request.Actor);
        var commandMode = options.CurrentValue.EffectiveCommandMode;
        var audit = BuildCancelOpenOrdersAudit(request, actor, commandMode);

        if (!options.CurrentValue.AllowsControlCommands)
        {
            return await BuildCommandResponseAsync(
                "control-room cancel open orders",
                audit,
                actor,
                "Disabled",
                "Control room commands are disabled by configuration or command mode.",
                success: false,
                exitCode: 2,
                stopwatch,
                cancellationToken).ConfigureAwait(false);
        }

        if (!HasConfirmation(request.ConfirmationText))
        {
            audit["confirmationRequired"] = true;
            return await BuildCommandResponseAsync(
                "control-room cancel open orders",
                audit,
                actor,
                "ConfirmationRequired",
                "Type CONFIRM before cancelling open orders from the control room.",
                success: false,
                exitCode: 3,
                stopwatch,
                cancellationToken).ConfigureAwait(false);
        }

        var orderRepository = serviceProvider.GetService<IOrderRepository>();
        var executionService = serviceProvider.GetService<IExecutionService>();
        if (orderRepository is null || executionService is null)
        {
            audit["orderRepositoryAvailable"] = orderRepository is not null;
            audit["executionServiceAvailable"] = executionService is not null;
            return await BuildCommandResponseAsync(
                "control-room cancel open orders",
                audit,
                actor,
                "Unsupported",
                "Open order cancellation requires both IOrderRepository and IExecutionService.",
                success: false,
                exitCode: 2,
                stopwatch,
                cancellationToken).ConfigureAwait(false);
        }

        IReadOnlyList<OrderDto> openOrders;
        try
        {
            openOrders = await orderRepository.GetOpenOrdersAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            audit["error"] = exception.Message;
            return await BuildCommandResponseAsync(
                "control-room cancel open orders",
                audit,
                actor,
                "Rejected",
                exception.Message,
                success: false,
                exitCode: 1,
                stopwatch,
                cancellationToken).ConfigureAwait(false);
        }

        var candidates = FilterOpenOrders(openOrders, request)
            .GroupBy(order => order.ClientOrderId, StringComparer.Ordinal)
            .Select(group => group.OrderByDescending(order => order.UpdatedAtUtc).First())
            .ToArray();

        audit["candidateCount"] = candidates.Length;
        audit["candidateClientOrderIds"] = candidates.Select(order => order.ClientOrderId).ToArray();

        if (candidates.Length == 0)
        {
            return await BuildCommandResponseAsync(
                "control-room cancel open orders",
                audit,
                actor,
                "NoOp",
                "No cancellable open orders matched the requested scope.",
                success: true,
                exitCode: 0,
                stopwatch,
                cancellationToken).ConfigureAwait(false);
        }

        var results = new List<Dictionary<string, object?>>(candidates.Length);
        foreach (var order in candidates)
        {
            if (string.IsNullOrWhiteSpace(order.ClientOrderId))
            {
                results.Add(BuildCancelResult(order, "Unsupported", false, "MISSING_CLIENT_ORDER_ID", "Order has no client order id."));
                continue;
            }

            try
            {
                var result = await executionService
                    .CancelOrderAsync(order.ClientOrderId, cancellationToken)
                    .ConfigureAwait(false);

                results.Add(BuildCancelResult(
                    order,
                    result.Status.ToString(),
                    result.Success,
                    result.ErrorCode,
                    result.ErrorMessage));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                results.Add(BuildCancelResult(order, "Exception", false, exception.GetType().Name, exception.Message));
            }
        }

        var acceptedCount = results.Count(result => result.TryGetValue("success", out var success) && success is true);
        audit["cancelResults"] = results;
        audit["acceptedCount"] = acceptedCount;
        audit["rejectedCount"] = results.Count - acceptedCount;

        var status = acceptedCount == results.Count
            ? "Accepted"
            : acceptedCount == 0 ? "Rejected" : "Partial";
        var message = status switch
        {
            "Accepted" => $"Cancelled {acceptedCount} open order(s).",
            "Partial" => $"Cancelled {acceptedCount} of {results.Count} open order(s).",
            _ => $"No open orders were cancelled. {results.Count} cancellation attempt(s) failed."
        };

        return await BuildCommandResponseAsync(
            "control-room cancel open orders",
            audit,
            actor,
            status,
            message,
            success: acceptedCount > 0,
            exitCode: acceptedCount == results.Count ? 0 : 1,
            stopwatch,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<IncidentPackage> ExportIncidentPackageAsync(
        IncidentPackageQuery query,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        query ??= new IncidentPackageQuery();
        var actor = ResolveActor(null);
        var audit = new Dictionary<string, object?>
        {
            ["source"] = "control-room-api",
            ["actor"] = actor,
            ["commandMode"] = options.CurrentValue.EffectiveCommandMode,
            ["query"] = query,
            ["readOnly"] = true
        };

        var snapshot = await queryService.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        var actions = await GetIncidentActionsAsync(cancellationToken).ConfigureAwait(false);

        audit["snapshotTimestampUtc"] = snapshot.TimestampUtc.ToString("O");
        audit["actionCount"] = actions.Actions.Count;
        stopwatch.Stop();
        audit["outcome"] = "Accepted";
        audit["durationMs"] = stopwatch.ElapsedMilliseconds;

        await LogAuditAsync(
            "control-room incident package export",
            audit,
            actor,
            success: true,
            exitCode: 0,
            durationMs: stopwatch.ElapsedMilliseconds,
            cancellationToken).ConfigureAwait(false);

        return new IncidentPackage(
            DateTimeOffset.UtcNow,
            IncidentPackageContractVersion,
            query,
            snapshot,
            actions,
            [
                IncidentRunbookPath
            ],
            BuildIncidentExportReferences(query));
    }

    public async Task<LiveArmingStatus> GetLiveArmingStatusAsync(CancellationToken cancellationToken = default)
    {
        var liveArmingService = serviceProvider.GetService<ILiveArmingService>();
        if (liveArmingService is null)
        {
            return CreateUnavailableLiveArmingStatus();
        }

        return await liveArmingService.GetStatusAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<ControlRoomCommandResponse> ArmLiveAsync(
        ArmLiveRequest request,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        ArgumentNullException.ThrowIfNull(request);
        var actor = ResolveActor(request.Actor);
        var commandMode = options.CurrentValue.EffectiveCommandMode;
        var audit = BuildLiveArmingAudit("arm", actor, commandMode, request.Reason, request.ConfirmationText);

        if (!options.CurrentValue.EnableControlCommands
            || !string.Equals(commandMode, ControlRoomCommandModes.LiveServices, StringComparison.Ordinal))
        {
            return await BuildCommandResponseAsync(
                "control-room live arm",
                audit,
                actor,
                "Disabled",
                "Live arming requires control-room command mode LiveServices.",
                success: false,
                exitCode: 2,
                stopwatch,
                cancellationToken).ConfigureAwait(false);
        }

        var liveArmingService = serviceProvider.GetService<ILiveArmingService>();
        if (liveArmingService is null)
        {
            audit["error"] = "Live arming service is unavailable.";
            return await BuildCommandResponseAsync(
                "control-room live arm",
                audit,
                actor,
                "Rejected",
                "Live arming service is unavailable. Enable AutotradeApi:EnableModules.",
                success: false,
                exitCode: 1,
                stopwatch,
                cancellationToken).ConfigureAwait(false);
        }

        LiveArmingResult result;
        try
        {
            result = await liveArmingService
                .ArmAsync(new LiveArmingRequest(actor, request.Reason, request.ConfirmationText), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            audit["error"] = exception.Message;
            return await BuildCommandResponseAsync(
                "control-room live arm",
                audit,
                actor,
                "Rejected",
                exception.Message,
                success: false,
                exitCode: 1,
                stopwatch,
                cancellationToken).ConfigureAwait(false);
        }

        audit["armingStatus"] = result.Status;
        audit["armingState"] = result.CurrentStatus.State;
        audit["blockingReasons"] = result.CurrentStatus.BlockingReasons;
        if (result.CurrentStatus.Evidence is not null)
        {
            audit["evidenceId"] = result.CurrentStatus.Evidence.EvidenceId;
            audit["expiresAtUtc"] = result.CurrentStatus.Evidence.ExpiresAtUtc.ToString("O");
        }

        return await BuildCommandResponseAsync(
            "control-room live arm",
            audit,
            actor,
            result.Status,
            result.Message,
            success: result.Accepted,
            exitCode: ResolveLiveArmingExitCode(result),
            stopwatch,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<ControlRoomCommandResponse> DisarmLiveAsync(
        DisarmLiveRequest request,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        ArgumentNullException.ThrowIfNull(request);
        var actor = ResolveActor(request.Actor);
        var commandMode = options.CurrentValue.EffectiveCommandMode;
        var audit = BuildLiveArmingAudit("disarm", actor, commandMode, request.Reason, request.ConfirmationText);

        var liveArmingService = serviceProvider.GetService<ILiveArmingService>();
        if (liveArmingService is null)
        {
            audit["error"] = "Live arming service is unavailable.";
            return await BuildCommandResponseAsync(
                "control-room live disarm",
                audit,
                actor,
                "Rejected",
                "Live arming service is unavailable. Enable AutotradeApi:EnableModules.",
                success: false,
                exitCode: 1,
                stopwatch,
                cancellationToken).ConfigureAwait(false);
        }

        LiveArmingResult result;
        try
        {
            result = await liveArmingService
                .DisarmAsync(new LiveDisarmingRequest(actor, request.Reason, request.ConfirmationText), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            audit["error"] = exception.Message;
            return await BuildCommandResponseAsync(
                "control-room live disarm",
                audit,
                actor,
                "Rejected",
                exception.Message,
                success: false,
                exitCode: 1,
                stopwatch,
                cancellationToken).ConfigureAwait(false);
        }

        audit["armingStatus"] = result.Status;
        audit["armingState"] = result.CurrentStatus.State;
        audit["blockingReasons"] = result.CurrentStatus.BlockingReasons;

        return await BuildCommandResponseAsync(
            "control-room live disarm",
            audit,
            actor,
            result.Status,
            result.Message,
            success: result.Accepted,
            exitCode: ResolveLiveArmingExitCode(result),
            stopwatch,
            cancellationToken).ConfigureAwait(false);
    }

    private static string? ResolveCancelOpenOrdersDisabledReason(
        bool hasOpenOrders,
        bool hasExecutionService,
        bool hasOrderRepository)
    {
        if (!hasOrderRepository)
        {
            return "Order repository is unavailable; open orders cannot be enumerated.";
        }

        if (!hasExecutionService)
        {
            return "Execution service is unavailable; exchange or paper cancellation is unsupported.";
        }

        return hasOpenOrders ? null : "No open orders are currently visible.";
    }

    private bool IsServiceAvailable<TService>()
    {
        if (serviceProvider is IServiceProviderIsService serviceAvailability)
        {
            return serviceAvailability.IsService(typeof(TService));
        }

        try
        {
            return serviceProvider.GetService<TService>() is not null;
        }
        catch
        {
            return false;
        }
    }

    private static IReadOnlyList<string> BuildIncidentExportReferences(IncidentPackageQuery query)
    {
        var references = new List<string>
        {
            "/api/control-room/snapshot",
            "/api/control-room/incidents/actions",
            "/api/audit-timeline"
        };

        if (!string.IsNullOrWhiteSpace(query.RiskEventId))
        {
            references.Add($"/api/control-room/risk/events/{Uri.EscapeDataString(query.RiskEventId.Trim())}");
            references.Add($"/api/control-room/risk/unhedged-exposures?riskEventId={Uri.EscapeDataString(query.RiskEventId.Trim())}");
        }

        return references;
    }

    private static Dictionary<string, object?> BuildCancelOpenOrdersAudit(
        CancelOpenOrdersRequest request,
        string actor,
        string commandMode)
    {
        return new Dictionary<string, object?>
        {
            ["source"] = "control-room-api",
            ["actor"] = actor,
            ["commandMode"] = commandMode,
            ["strategyId"] = request.StrategyId,
            ["marketId"] = request.MarketId,
            ["reasonCode"] = request.ReasonCode,
            ["reason"] = request.Reason,
            ["confirmationProvided"] = !string.IsNullOrWhiteSpace(request.ConfirmationText)
        };
    }

    private static IEnumerable<OrderDto> FilterOpenOrders(
        IEnumerable<OrderDto> openOrders,
        CancelOpenOrdersRequest request)
    {
        return openOrders.Where(order =>
            MatchesOptionalScope(order.StrategyId, request.StrategyId)
            && MatchesOptionalScope(order.MarketId, request.MarketId));
    }

    private static bool MatchesOptionalScope(string? value, string? expected)
    {
        return string.IsNullOrWhiteSpace(expected)
            || string.Equals(value, expected.Trim(), StringComparison.Ordinal);
    }

    private static Dictionary<string, object?> BuildCancelResult(
        OrderDto order,
        string status,
        bool success,
        string? errorCode,
        string? errorMessage)
    {
        return new Dictionary<string, object?>
        {
            ["orderId"] = order.Id,
            ["clientOrderId"] = order.ClientOrderId,
            ["strategyId"] = order.StrategyId,
            ["marketId"] = order.MarketId,
            ["status"] = status,
            ["success"] = success,
            ["errorCode"] = errorCode,
            ["errorMessage"] = errorMessage
        };
    }

    private async Task<ArcPaperAutoTradeResponse> BuildArcPaperAutoTradeResponseAsync(
        Dictionary<string, object?> audit,
        string actor,
        string status,
        string message,
        ArcAccessDecision decision,
        ControlRoomCommandResponse? command,
        bool success,
        int exitCode,
        Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        stopwatch.Stop();
        audit["outcome"] = status;
        audit["durationMs"] = stopwatch.ElapsedMilliseconds;

        await LogAuditAsync(
            "arc paper autotrade permission",
            audit,
            actor,
            success,
            exitCode,
            stopwatch.ElapsedMilliseconds,
            cancellationToken).ConfigureAwait(false);

        return new ArcPaperAutoTradeResponse(status, message, decision, command);
    }

    private static Dictionary<string, object?> BuildArcPaperAutoTradeAudit(
        string strategyId,
        ArcPaperAutoTradeRequest request,
        string actor,
        string commandMode)
        => new()
        {
            ["source"] = "control-room-api",
            ["actor"] = actor,
            ["commandMode"] = commandMode,
            ["strategyId"] = strategyId,
            ["walletAddress"] = request.WalletAddress,
            ["requiredPermission"] = ArcEntitlementPermission.RequestPaperAutoTrade.ToString(),
            ["reason"] = request.Reason,
            ["confirmationProvided"] = !string.IsNullOrWhiteSpace(request.ConfirmationText),
            ["targetState"] = StrategyState.Running.ToString()
        };

    private static void AddAccessDecisionAudit(
        Dictionary<string, object?> audit,
        ArcAccessDecision decision)
    {
        audit["accessAllowed"] = decision.Allowed;
        audit["accessReasonCode"] = decision.ReasonCode;
        audit["accessTier"] = decision.Tier;
        audit["accessExpiresAtUtc"] = decision.ExpiresAtUtc?.ToString("O");
        audit["accessEvidenceTransactionHash"] = decision.EvidenceTransactionHash;
    }

    private static ArcAccessDecision CreateFallbackDecision(
        string strategyId,
        string? walletAddress,
        string reasonCode)
        => new(
            Allowed: false,
            reasonCode,
            "Arc access decision service is unavailable.",
            ArcEntitlementPermission.RequestPaperAutoTrade,
            strategyId,
            walletAddress,
            "arc-paper-autotrade",
            strategyId);

    private async Task<ControlRoomCommandResponse> BuildCommandResponseAsync(
        string commandName,
        Dictionary<string, object?> audit,
        string actor,
        string status,
        string message,
        bool success,
        int exitCode,
        Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        stopwatch.Stop();
        audit["outcome"] = status;
        audit["durationMs"] = stopwatch.ElapsedMilliseconds;

        await LogAuditAsync(
            commandName,
            audit,
            actor,
            success,
            exitCode,
            stopwatch.ElapsedMilliseconds,
            cancellationToken).ConfigureAwait(false);

        var snapshot = await queryService.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        return new ControlRoomCommandResponse(
            status,
            options.CurrentValue.EffectiveCommandMode,
            message,
            snapshot);
    }

    private async Task LogAuditAsync(
        string commandName,
        Dictionary<string, object?> audit,
        string actor,
        bool success,
        int exitCode,
        long durationMs,
        CancellationToken cancellationToken)
    {
        var auditLogger = serviceProvider.GetService<ICommandAuditLogger>();
        if (auditLogger is null)
        {
            return;
        }

        var entry = new CommandAuditEntry(
            commandName,
            JsonSerializer.Serialize(audit),
            actor,
            success,
            exitCode,
            durationMs,
            DateTimeOffset.UtcNow);

        try
        {
            await auditLogger.LogAsync(entry, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // Command execution must not depend on audit storage availability.
        }
    }

    private static KillSwitchLevel NormalizeKillSwitchLevel(bool active, string? requestedLevel)
    {
        if (!active)
        {
            return KillSwitchLevel.None;
        }

        if (string.IsNullOrWhiteSpace(requestedLevel))
        {
            return KillSwitchLevel.HardStop;
        }

        return requestedLevel.Trim().Equals("SoftStop", StringComparison.OrdinalIgnoreCase)
            ? KillSwitchLevel.SoftStop
            : KillSwitchLevel.HardStop;
    }

    private static bool RequiresStrategyConfirmation(StrategyState targetState, string commandMode)
    {
        return targetState == StrategyState.Running
            && string.Equals(commandMode, ControlRoomCommandModes.LiveServices, StringComparison.Ordinal);
    }

    private static bool RequiresKillSwitchConfirmation(bool active, KillSwitchLevel level)
    {
        return !active || level == KillSwitchLevel.HardStop;
    }

    private static bool HasConfirmation(string? confirmationText)
    {
        return string.Equals(confirmationText?.Trim(), ConfirmationText, StringComparison.OrdinalIgnoreCase);
    }

    private static int ResolveLiveArmingExitCode(LiveArmingResult result)
    {
        if (result.Accepted)
        {
            return 0;
        }

        return string.Equals(result.Status, "ConfirmationRequired", StringComparison.Ordinal)
            ? 3
            : 1;
    }

    private static LiveArmingStatus CreateUnavailableLiveArmingStatus()
    {
        const string reason = "Live arming service is unavailable. Enable AutotradeApi:EnableModules.";
        return new LiveArmingStatus(
            false,
            "Unavailable",
            reason,
            "unknown",
            DateTimeOffset.UtcNow,
            null,
            [reason]);
    }

    private static string ResolveActor(string? actor)
    {
        return string.IsNullOrWhiteSpace(actor)
            ? Environment.UserName
            : actor.Trim();
    }

    private static Dictionary<string, object?> BuildStrategyStateAudit(
        string strategyId,
        SetStrategyStateRequest request,
        string actor,
        string commandMode)
    {
        return new Dictionary<string, object?>
        {
            ["source"] = "control-room-api",
            ["actor"] = actor,
            ["commandMode"] = commandMode,
            ["strategyId"] = strategyId,
            ["targetState"] = request.TargetState,
            ["reasonCode"] = request.ReasonCode,
            ["reason"] = request.Reason,
            ["confirmationProvided"] = !string.IsNullOrWhiteSpace(request.ConfirmationText)
        };
    }

    private static Dictionary<string, object?> BuildKillSwitchAudit(
        SetKillSwitchRequest request,
        string actor,
        string commandMode,
        KillSwitchLevel level)
    {
        return new Dictionary<string, object?>
        {
            ["source"] = "control-room-api",
            ["actor"] = actor,
            ["commandMode"] = commandMode,
            ["active"] = request.Active,
            ["level"] = level.ToString(),
            ["reasonCode"] = request.ReasonCode,
            ["reason"] = request.Reason,
            ["confirmationProvided"] = !string.IsNullOrWhiteSpace(request.ConfirmationText)
        };
    }

    private static Dictionary<string, object?> BuildLiveArmingAudit(
        string action,
        string actor,
        string commandMode,
        string? reason,
        string? confirmationText)
    {
        return new Dictionary<string, object?>
        {
            ["source"] = "control-room-api",
            ["actor"] = actor,
            ["commandMode"] = commandMode,
            ["action"] = action,
            ["reason"] = reason,
            ["confirmationProvided"] = !string.IsNullOrWhiteSpace(confirmationText)
        };
    }
}
