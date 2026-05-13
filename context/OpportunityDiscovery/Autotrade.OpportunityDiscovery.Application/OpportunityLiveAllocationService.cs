using System.Text.Json;
using System.Text.Json.Serialization;
using Autotrade.OpportunityDiscovery.Application.Contract;
using Autotrade.OpportunityDiscovery.Domain.Entities;
using Autotrade.OpportunityDiscovery.Domain.Exceptions;
using Autotrade.OpportunityDiscovery.Domain.Shared.Enums;
using Autotrade.Trading.Application.Contract.Accounts;
using Autotrade.Trading.Application.Contract.Compliance;
using Autotrade.Trading.Application.Contract.Execution;
using Autotrade.Trading.Application.Contract.Repositories;
using Autotrade.Trading.Application.Contract.Risk;
using Microsoft.Extensions.Options;

namespace Autotrade.OpportunityDiscovery.Application;

public sealed class OpportunityLiveAllocationService : IOpportunityLiveAllocationService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly IOpportunityV2Repository _repository;
    private readonly IReadOnlyList<ILiveArmingService> _liveArmingServices;
    private readonly IReadOnlyList<IAccountSyncService> _accountSyncServices;
    private readonly IReadOnlyList<IRiskManager> _riskManagers;
    private readonly IReadOnlyList<IComplianceGuard> _complianceGuards;
    private readonly IReadOnlyList<IOrderRepository> _orderRepositories;
    private readonly IReadOnlyList<IExecutionService> _executionServices;
    private readonly OpportunityLiveAllocationOptions _options;
    private readonly ComplianceOptions _complianceOptions;

    public OpportunityLiveAllocationService(
        IOpportunityV2Repository repository,
        IEnumerable<ILiveArmingService> liveArmingServices,
        IEnumerable<IAccountSyncService> accountSyncServices,
        IEnumerable<IRiskManager> riskManagers,
        IEnumerable<IComplianceGuard> complianceGuards,
        IEnumerable<IOrderRepository> orderRepositories,
        IEnumerable<IExecutionService> executionServices,
        IOptions<OpportunityLiveAllocationOptions> options,
        IOptions<ComplianceOptions> complianceOptions)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _liveArmingServices = (liveArmingServices ?? throw new ArgumentNullException(nameof(liveArmingServices))).ToArray();
        _accountSyncServices = (accountSyncServices ?? throw new ArgumentNullException(nameof(accountSyncServices))).ToArray();
        _riskManagers = (riskManagers ?? throw new ArgumentNullException(nameof(riskManagers))).ToArray();
        _complianceGuards = (complianceGuards ?? throw new ArgumentNullException(nameof(complianceGuards))).ToArray();
        _orderRepositories = (orderRepositories ?? throw new ArgumentNullException(nameof(orderRepositories))).ToArray();
        _executionServices = (executionServices ?? throw new ArgumentNullException(nameof(executionServices))).ToArray();
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _complianceOptions = complianceOptions?.Value ?? throw new ArgumentNullException(nameof(complianceOptions));
    }

    public async Task<OpportunityLiveAllocationResult> TryCreateMicroAllocationAsync(
        OpportunityLiveAllocationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateAllocationRequest(request);
        var now = DateTimeOffset.UtcNow;
        var blockingReasons = new List<string>();
        var hypothesis = await _repository.GetHypothesisAsync(request.HypothesisId, cancellationToken).ConfigureAwait(false);
        var policy = await _repository.GetExecutablePolicyAsync(request.ExecutablePolicyId, cancellationToken).ConfigureAwait(false);
        var gates = await _repository.ListPromotionGatesAsync(request.HypothesisId, cancellationToken).ConfigureAwait(false);
        var latestRequiredGates = LatestRequiredGates(gates);
        blockingReasons.AddRange(RequiredGateBlockingReasons(latestRequiredGates));

        if (hypothesis is null)
        {
            blockingReasons.Add("hypothesis was not found");
        }
        else if (hypothesis.Status != OpportunityHypothesisStatus.LiveEligible)
        {
            blockingReasons.Add($"hypothesis must be LiveEligible before auto Live allocation; actual={hypothesis.Status}");
        }

        if (policy is null)
        {
            blockingReasons.Add("executable policy was not found");
        }
        else
        {
            if (policy.HypothesisId != request.HypothesisId)
            {
                blockingReasons.Add("executable policy does not belong to the hypothesis");
            }

            if (!policy.IsExecutableAt(now))
            {
                blockingReasons.Add($"executable policy must be active and valid at allocation time; actual={policy.Status}");
            }
        }

        var activeAllocations = await _repository.ListActiveLiveAllocationsAsync(now, cancellationToken).ConfigureAwait(false);
        blockingReasons.AddRange(await EvaluateReadinessAsync(
                request,
                policy,
                activeAllocations,
                now,
                cancellationToken)
            .ConfigureAwait(false));
        var metricsJson = BuildMetricsJson(
            "auto-live-allocation",
            blockingReasons,
            new
            {
                request,
                options = _options,
                activeLiveExposure = activeAllocations.Sum(item => item.MaxNotional),
                activeLiveOpportunities = activeAllocations.Select(item => item.HypothesisId).Distinct().Count(),
                hypothesisStatus = hypothesis?.Status,
                policyStatus = policy?.Status
            });

        if (blockingReasons.Count > 0 || hypothesis is null || policy is null)
        {
            return new OpportunityLiveAllocationResult(
                false,
                null,
                hypothesis is null ? null : OpportunityV2Mapper.ToDto(hypothesis),
                null,
                blockingReasons,
                metricsJson);
        }

        try
        {
            var allocation = new OpportunityLiveAllocation(
                request.HypothesisId,
                policy.Id,
                request.RequestedMaxNotional,
                request.RequestedMaxContracts,
                request.ValidUntilUtc,
                request.Reason,
                now);
            var transition = hypothesis.PublishLive(
                latestRequiredGates,
                policy.Id,
                allocation.Id,
                string.IsNullOrWhiteSpace(request.Actor) ? "opportunity-live-allocation" : request.Actor,
                string.IsNullOrWhiteSpace(request.Reason) ? "auto Live micro-allocation created" : request.Reason,
                request.EvidenceIds,
                now);
            await _repository
                .AddLiveAllocationWithHypothesisTransitionAsync(allocation, hypothesis, transition, cancellationToken)
                .ConfigureAwait(false);
            return new OpportunityLiveAllocationResult(
                true,
                OpportunityV2Mapper.ToDto(allocation),
                OpportunityV2Mapper.ToDto(hypothesis),
                OpportunityV2Mapper.ToDto(transition),
                [],
                BuildMetricsJson(
                    "auto-live-allocation",
                    [],
                    new { request, options = _options, allocationId = allocation.Id }));
        }
        catch (OpportunityLifecycleException ex)
        {
            return new OpportunityLiveAllocationResult(
                false,
                null,
                OpportunityV2Mapper.ToDto(hypothesis),
                null,
                [ex.Message],
                BuildMetricsJson("auto-live-allocation", [ex.Message], new { request, options = _options }));
        }
    }

    public async Task<OpportunitySuspensionResult> SuspendIfKillCriteriaAsync(
        OpportunitySuspensionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.HypothesisId == Guid.Empty)
        {
            throw new ArgumentException("HypothesisId cannot be empty.", nameof(request));
        }

        if (request.ExecutablePolicyId == Guid.Empty)
        {
            throw new ArgumentException("ExecutablePolicyId cannot be empty.", nameof(request));
        }

        var killReasons = EvaluateKillCriteria(request.Metrics);
        var metricsJson = BuildMetricsJson("suspension", killReasons, new { request.Metrics, options = _options });
        if (killReasons.Count == 0)
        {
            return new OpportunitySuspensionResult(false, null, null, null, [], [], metricsJson);
        }

        var now = DateTimeOffset.UtcNow;
        var hypothesis = await _repository.GetHypothesisAsync(request.HypothesisId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Hypothesis was not found.");
        var policy = await _repository.GetExecutablePolicyAsync(request.ExecutablePolicyId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Executable policy was not found.");
        var allocation = request.LiveAllocationId.HasValue
            ? await _repository.GetLiveAllocationAsync(request.LiveAllocationId.Value, cancellationToken).ConfigureAwait(false)
            : (await _repository.ListActiveLiveAllocationsAsync(now, cancellationToken).ConfigureAwait(false))
                .FirstOrDefault(item => item.ExecutablePolicyId == policy.Id);
        var reason = string.Join("; ", killReasons);
        policy.Suspend(now);
        allocation?.Suspend(reason, now);
        var transition = hypothesis.Suspend(
            string.IsNullOrWhiteSpace(request.Actor) ? "opportunity-live-allocation" : request.Actor,
            string.IsNullOrWhiteSpace(request.Reason) ? reason : request.Reason + ": " + reason,
            request.EvidenceIds,
            now);

        await _repository
            .SuspendLiveOpportunityAsync(hypothesis, transition, policy, allocation, cancellationToken)
            .ConfigureAwait(false);
        var canceledOrders = await CancelOpenOrdersAsync(
                request.StrategyId,
                string.IsNullOrWhiteSpace(request.MarketId) ? policy.MarketId : request.MarketId,
                cancellationToken)
            .ConfigureAwait(false);

        return new OpportunitySuspensionResult(
            true,
            OpportunityV2Mapper.ToDto(hypothesis),
            allocation is null ? null : OpportunityV2Mapper.ToDto(allocation),
            OpportunityV2Mapper.ToDto(transition),
            killReasons,
            canceledOrders,
            BuildMetricsJson("suspension", killReasons, new
            {
                request.Metrics,
                options = _options,
                policyId = policy.Id,
                allocationId = allocation?.Id,
                canceledOrders
            }));
    }

    private async Task<IReadOnlyList<string>> EvaluateReadinessAsync(
        OpportunityLiveAllocationRequest request,
        ExecutableOpportunityPolicy? policy,
        IReadOnlyList<OpportunityLiveAllocation> activeAllocations,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var blockingReasons = new List<string>();
        AddLimitFailures(blockingReasons, request, policy, activeAllocations);
        AddConfiguredLimitIncreaseFailures(blockingReasons);
        var liveArming = _liveArmingServices.FirstOrDefault();
        if (liveArming is null)
        {
            blockingReasons.Add("live arming service is not configured");
        }
        else
        {
            var status = await liveArming.GetStatusAsync(cancellationToken).ConfigureAwait(false);
            if (!status.IsArmed)
            {
                blockingReasons.Add("Live arming must be active before auto Live allocation: " + status.Reason);
            }
        }

        var accountSync = _accountSyncServices.FirstOrDefault();
        if (accountSync is null)
        {
            blockingReasons.Add("account sync service is not configured");
        }
        else if (accountSync.LastSyncTime is null)
        {
            blockingReasons.Add("account sync has not completed");
        }
        else
        {
            var age = now - accountSync.LastSyncTime.Value;
            if (age > TimeSpan.FromSeconds(Math.Max(1, _options.MaxAccountSyncAgeSeconds)))
            {
                blockingReasons.Add($"account sync is stale: age={age.TotalSeconds:0}s");
            }
        }

        var risk = _riskManagers.FirstOrDefault();
        if (risk is null)
        {
            blockingReasons.Add("risk manager is not configured");
        }
        else
        {
            var snapshot = risk.GetStateSnapshot();
            if (risk.IsKillSwitchActive || risk.GetKillSwitchState().IsActive)
            {
                blockingReasons.Add("risk kill switch is active");
            }

            if (snapshot.TotalOpenNotional + request.RequestedMaxNotional > _options.GlobalActiveLiveExposureUsdc)
            {
                blockingReasons.Add(
                    $"risk open notional plus requested allocation exceeds global live exposure limit: {snapshot.TotalOpenNotional + request.RequestedMaxNotional:0.####}/{_options.GlobalActiveLiveExposureUsdc:0.####}");
            }
        }

        var compliance = _complianceGuards.FirstOrDefault();
        if (compliance is null)
        {
            blockingReasons.Add("compliance guard is not configured");
        }
        else
        {
            AddComplianceFailures(blockingReasons, compliance.CheckConfiguration(ExecutionMode.Live), "configuration");
            AddComplianceFailures(blockingReasons, compliance.CheckOrderPlacement(ExecutionMode.Live), "order placement");
        }

        return blockingReasons;
    }

    private void AddLimitFailures(
        ICollection<string> blockingReasons,
        OpportunityLiveAllocationRequest request,
        ExecutableOpportunityPolicy? policy,
        IReadOnlyList<OpportunityLiveAllocation> activeAllocations)
    {
        if (policy is not null && policy.MaxNotional > _options.SingleOrderMaxNotionalUsdc)
        {
            blockingReasons.Add(
                $"single order policy max notional {policy.MaxNotional:0.####} exceeds limit {_options.SingleOrderMaxNotionalUsdc:0.####}");
        }

        if (request.RequestedMaxNotional > _options.SingleOpportunityMaxNotionalUsdc)
        {
            blockingReasons.Add(
                $"single opportunity allocation {request.RequestedMaxNotional:0.####} exceeds limit {_options.SingleOpportunityMaxNotionalUsdc:0.####}");
        }

        if (request.RequestedMaxNotional > _options.SingleCycleMaxNotionalUsdc)
        {
            blockingReasons.Add(
                $"single cycle allocation {request.RequestedMaxNotional:0.####} exceeds limit {_options.SingleCycleMaxNotionalUsdc:0.####}");
        }

        var activeExposure = activeAllocations.Sum(item => item.MaxNotional);
        if (activeExposure + request.RequestedMaxNotional > _options.GlobalActiveLiveExposureUsdc)
        {
            blockingReasons.Add(
                $"global active live exposure would exceed limit: {activeExposure + request.RequestedMaxNotional:0.####}/{_options.GlobalActiveLiveExposureUsdc:0.####}");
        }

        var activeOpportunities = activeAllocations.Select(item => item.HypothesisId).Distinct().Count();
        if (activeOpportunities >= _options.MaxActiveLiveOpportunities)
        {
            blockingReasons.Add(
                $"active Live opportunity count {activeOpportunities} has reached limit {_options.MaxActiveLiveOpportunities}");
        }
    }

    private void AddConfiguredLimitIncreaseFailures(ICollection<string> blockingReasons)
    {
        var increased = _options.SingleOrderMaxNotionalUsdc > OpportunityLiveAllocationOptions.DefaultSingleOrderNotionalUsdc
            || _options.SingleOpportunityMaxNotionalUsdc > OpportunityLiveAllocationOptions.DefaultSingleOpportunityNotionalUsdc
            || _options.SingleCycleMaxNotionalUsdc > OpportunityLiveAllocationOptions.DefaultSingleCycleNotionalUsdc
            || _options.GlobalActiveLiveExposureUsdc > OpportunityLiveAllocationOptions.DefaultGlobalActiveLiveExposureUsdc
            || _options.MaxActiveLiveOpportunities > OpportunityLiveAllocationOptions.DefaultMaxActiveLiveOpportunities;
        if (increased && !_complianceOptions.AllowUnsafeLiveParameters)
        {
            blockingReasons.Add("increasing default Live allocation limits requires Compliance.AllowUnsafeLiveParameters=true");
        }
    }

    private static void AddComplianceFailures(
        ICollection<string> blockingReasons,
        ComplianceCheckResult result,
        string checkName)
    {
        if (!result.IsCompliant || result.BlocksOrders)
        {
            var issues = result.Issues
                .Where(issue => issue.BlocksLiveOrders || issue.Severity == ComplianceSeverity.Error)
                .Select(issue => $"{issue.Code}: {issue.Message}")
                .DefaultIfEmpty($"{checkName} compliance check failed");
            foreach (var issue in issues)
            {
                blockingReasons.Add("compliance " + checkName + " blocked Live allocation: " + issue);
            }
        }
    }

    private IReadOnlyList<string> EvaluateKillCriteria(OpportunityKillCriteriaMetrics metrics)
    {
        var killReasons = new List<string>();
        if (metrics.RealizedEdge < _options.MinRealizedEdge)
        {
            killReasons.Add($"realized edge {metrics.RealizedEdge:0.####} is below threshold {_options.MinRealizedEdge:0.####}");
        }

        if (metrics.PredictedEdge <= 0m)
        {
            killReasons.Add("predicted edge must stay positive");
        }
        else
        {
            var adverseRatio = metrics.AdverseSlippage <= 0m ? 0m : metrics.AdverseSlippage / metrics.PredictedEdge;
            if (adverseRatio > _options.MaxAdverseSlippageToPredictedEdgeRatio)
            {
                killReasons.Add(
                    $"adverse slippage consumed {adverseRatio:P2} of predicted edge; max={_options.MaxAdverseSlippageToPredictedEdgeRatio:P2}");
            }
        }

        if (metrics.FillRate < _options.MinFillRate)
        {
            killReasons.Add($"fill rate {metrics.FillRate:P2} is below threshold {_options.MinFillRate:P2}");
        }

        if (metrics.DrawdownUsdc > _options.MaxDrawdownUsdc)
        {
            killReasons.Add($"drawdown {metrics.DrawdownUsdc:0.####} exceeds max {_options.MaxDrawdownUsdc:0.####}");
        }

        if (metrics.SourceDriftScore > _options.MaxSourceDriftScore)
        {
            killReasons.Add($"source drift {metrics.SourceDriftScore:0.####} exceeds max {_options.MaxSourceDriftScore:0.####}");
        }

        if (metrics.CalibrationDriftScore > _options.MaxCalibrationDriftScore)
        {
            killReasons.Add($"model calibration drift {metrics.CalibrationDriftScore:0.####} exceeds max {_options.MaxCalibrationDriftScore:0.####}");
        }

        if (metrics.OrderBookAgeSeconds > _options.MaxOrderBookAgeSeconds)
        {
            killReasons.Add($"stale order book age {metrics.OrderBookAgeSeconds}s exceeds max {_options.MaxOrderBookAgeSeconds}s");
        }

        if (metrics.RiskEventCount > 0)
        {
            killReasons.Add($"risk event count must be 0; actual={metrics.RiskEventCount}");
        }

        if (metrics.ComplianceEventCount > 0)
        {
            killReasons.Add($"compliance event count must be 0; actual={metrics.ComplianceEventCount}");
        }

        return killReasons;
    }

    private async Task<IReadOnlyList<OpportunityCanceledOrderDto>> CancelOpenOrdersAsync(
        string? strategyId,
        string? marketId,
        CancellationToken cancellationToken)
    {
        var orderRepository = _orderRepositories.FirstOrDefault();
        var executionService = _executionServices.FirstOrDefault();
        if (orderRepository is null || executionService is null || string.IsNullOrWhiteSpace(marketId))
        {
            return [];
        }

        var openOrders = await orderRepository.GetOpenOrdersAsync(cancellationToken).ConfigureAwait(false);
        var matches = openOrders
            .Where(order => string.Equals(order.MarketId, marketId, StringComparison.OrdinalIgnoreCase))
            .Where(order => string.IsNullOrWhiteSpace(strategyId)
                || string.Equals(order.StrategyId, strategyId, StringComparison.OrdinalIgnoreCase))
            .Where(order => !string.IsNullOrWhiteSpace(order.ClientOrderId))
            .ToArray();
        var canceled = new List<OpportunityCanceledOrderDto>();
        foreach (var order in matches)
        {
            try
            {
                var result = await executionService.CancelOrderAsync(order.ClientOrderId!, cancellationToken).ConfigureAwait(false);
                canceled.Add(new OpportunityCanceledOrderDto(
                    order.ClientOrderId!,
                    order.ExchangeOrderId,
                    order.MarketId,
                    order.StrategyId,
                    result.Success,
                    result.Status.ToString(),
                    result.ErrorMessage));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                canceled.Add(new OpportunityCanceledOrderDto(
                    order.ClientOrderId!,
                    order.ExchangeOrderId,
                    order.MarketId,
                    order.StrategyId,
                    false,
                    "CancelFailed",
                    ex.Message));
            }
        }

        return canceled;
    }

    private static IReadOnlyList<OpportunityPromotionGate> LatestRequiredGates(IReadOnlyList<OpportunityPromotionGate> gates)
    {
        var byKind = gates
            .GroupBy(gate => gate.GateKind)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(gate => gate.EvaluatedAtUtc).First());
        return OpportunityHypothesis.RequiredLiveGateKinds
            .OrderBy(kind => kind)
            .Where(byKind.ContainsKey)
            .Select(kind => byKind[kind])
            .ToArray();
    }

    private static IReadOnlyList<string> RequiredGateBlockingReasons(IReadOnlyList<OpportunityPromotionGate> latestRequiredGates)
    {
        var byKind = latestRequiredGates.ToDictionary(gate => gate.GateKind);
        var blockingReasons = new List<string>();
        foreach (var kind in OpportunityHypothesis.RequiredLiveGateKinds.OrderBy(kind => kind))
        {
            if (!byKind.TryGetValue(kind, out var gate))
            {
                blockingReasons.Add($"missing required {kind} gate");
                continue;
            }

            if (gate.Status != OpportunityPromotionGateStatus.Passed)
            {
                blockingReasons.Add($"latest required {kind} gate is {gate.Status}: {gate.Reason}");
            }
        }

        return blockingReasons;
    }

    private static void ValidateAllocationRequest(OpportunityLiveAllocationRequest request)
    {
        if (request.HypothesisId == Guid.Empty)
        {
            throw new ArgumentException("HypothesisId cannot be empty.", nameof(request));
        }

        if (request.ExecutablePolicyId == Guid.Empty)
        {
            throw new ArgumentException("ExecutablePolicyId cannot be empty.", nameof(request));
        }

        if (request.RequestedMaxNotional <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "RequestedMaxNotional must be positive.");
        }

        if (request.RequestedMaxContracts <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "RequestedMaxContracts must be positive.");
        }

        if (request.ValidUntilUtc <= DateTimeOffset.UtcNow)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "ValidUntilUtc must be in the future.");
        }
    }

    private static string BuildMetricsJson(string action, IReadOnlyList<string> blockingReasons, object payload)
        => JsonSerializer.Serialize(
            new
            {
                action,
                passed = blockingReasons.Count == 0,
                blockingReasons,
                payload
            },
            JsonOptions);
}
