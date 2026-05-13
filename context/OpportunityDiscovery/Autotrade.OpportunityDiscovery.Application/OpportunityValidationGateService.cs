using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Autotrade.MarketData.Application.Contract.Tape;
using Autotrade.OpportunityDiscovery.Application.Contract;
using Autotrade.OpportunityDiscovery.Domain.Entities;
using Autotrade.OpportunityDiscovery.Domain.Exceptions;
using Autotrade.OpportunityDiscovery.Domain.Shared.Enums;
using Autotrade.Trading.Application.Contract.Execution;

namespace Autotrade.OpportunityDiscovery.Application;

public sealed class OpportunityValidationGateService : IOpportunityValidationGateService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly IOpportunityV2Repository _repository;
    private readonly IMarketReplayBacktestRunner _backtestRunner;
    private readonly IReadOnlyList<IOpportunityPaperValidationSource> _paperValidationSources;
    private readonly IReadOnlyList<ILiveArmingService> _liveArmingServices;

    public OpportunityValidationGateService(
        IOpportunityV2Repository repository,
        IMarketReplayBacktestRunner backtestRunner,
        IEnumerable<IOpportunityPaperValidationSource> paperValidationSources,
        IEnumerable<ILiveArmingService> liveArmingServices)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _backtestRunner = backtestRunner ?? throw new ArgumentNullException(nameof(backtestRunner));
        _paperValidationSources = (paperValidationSources ?? throw new ArgumentNullException(nameof(paperValidationSources))).ToArray();
        _liveArmingServices = (liveArmingServices ?? throw new ArgumentNullException(nameof(liveArmingServices))).ToArray();
    }

    public async Task<OpportunityGateEvaluationResult> EvaluateBacktestAsync(
        OpportunityBacktestGateRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateHypothesisId(request.HypothesisId);
        ArgumentNullException.ThrowIfNull(request.ReplayRequest);

        var thresholds = request.Thresholds ?? new OpportunityValidationThresholds();
        var startedAtUtc = DateTimeOffset.UtcNow;
        var replaySeed = BuildStableSeed(new
        {
            kind = OpportunityEvaluationKind.Backtest,
            request.HypothesisId,
            request.RunVersion,
            request.MarketTapeSliceId,
            request.EvidenceAsOfUtc,
            request.ReplayRequest,
            request.SampleMetrics,
            request.PredictedEdge
        });
        var run = new OpportunityEvaluationRun(
            request.HypothesisId,
            OpportunityEvaluationKind.Backtest,
            request.RunVersion,
            request.MarketTapeSliceId,
            replaySeed,
            startedAtUtc);
        var blockingReasons = new List<string>();
        MarketReplayBacktestResult? result = null;

        if (request.EvidenceAsOfUtc > request.ReplayRequest.AsOfUtc)
        {
            blockingReasons.Add("future evidence is forbidden: evidence timestamp is after replay as-of timestamp");
        }

        if (request.ReplayRequest.ToUtc > request.ReplayRequest.AsOfUtc)
        {
            blockingReasons.Add("future price data is forbidden: replay end timestamp is after replay as-of timestamp");
        }

        if (blockingReasons.Count == 0)
        {
            try
            {
                result = await _backtestRunner
                    .RunAsync(request.ReplayRequest, cancellationToken)
                    .ConfigureAwait(false);
                AddSampleMetricFailures(blockingReasons, request.SampleMetrics, thresholds);
                if (!result.Entered)
                {
                    blockingReasons.Add("backtest did not enter a position");
                }

                if (!result.Exited)
                {
                    blockingReasons.Add("backtest did not produce an exit or terminal mark");
                }

                if (result.NetPnl <= 0m)
                {
                    blockingReasons.Add("backtest net PnL must be greater than 0");
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                blockingReasons.Add($"backtest runner failed: {ex.Message}");
                run.MarkFailed(ex.Message, DateTimeOffset.UtcNow);
            }
        }
        else
        {
            AddSampleMetricFailures(blockingReasons, request.SampleMetrics, thresholds);
        }

        var metricsJson = BuildMetricsJson(
            "backtest",
            thresholds,
            blockingReasons,
            new
            {
                replaySeed,
                replayRequest = request.ReplayRequest,
                request.EvidenceAsOfUtc,
                request.SampleMetrics,
                request.PredictedEdge,
                backtest = result
            });
        if (run.Status == OpportunityEvaluationRunStatus.Running)
        {
            run.MarkSucceeded(metricsJson, DateTimeOffset.UtcNow);
        }

        return await PersistEvaluationAsync(
                run,
                request.HypothesisId,
                OpportunityPromotionGateKind.Backtest,
                "opportunity-validation-gates/backtest-v1",
                blockingReasons,
                metricsJson,
                request.EvidenceIds,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<OpportunityGateEvaluationResult> EvaluateShadowAsync(
        OpportunityShadowGateRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateHypothesisId(request.HypothesisId);
        var thresholds = request.Thresholds ?? new OpportunityValidationThresholds();
        var blockingReasons = EvaluateObservedMetrics(
            request.Metrics,
            thresholds,
            requirePositivePnl: false,
            requireSlippageBudget: true);
        var run = CreateSucceededRun(
            request.HypothesisId,
            OpportunityEvaluationKind.Shadow,
            request.RunVersion,
            request.MarketTapeSliceId,
            new
            {
                kind = OpportunityEvaluationKind.Shadow,
                request.HypothesisId,
                request.RunVersion,
                request.MarketTapeSliceId,
                request.Metrics
            },
            BuildMetricsJson("shadow", thresholds, blockingReasons, new { request.Metrics }));

        return await PersistEvaluationAsync(
                run,
                request.HypothesisId,
                OpportunityPromotionGateKind.Shadow,
                "opportunity-validation-gates/shadow-v1",
                blockingReasons,
                run.ResultJson,
                request.EvidenceIds,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<OpportunityGateEvaluationResult> EvaluatePaperAsync(
        OpportunityPaperGateRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateHypothesisId(request.HypothesisId);
        if (request.PaperRunSessionId == Guid.Empty)
        {
            throw new ArgumentException("PaperRunSessionId cannot be empty.", nameof(request));
        }

        var thresholds = request.Thresholds ?? new OpportunityValidationThresholds();
        var source = _paperValidationSources.FirstOrDefault();
        var blockingReasons = new List<string>();
        OpportunityPaperValidationSnapshot? paper = null;
        if (source is null)
        {
            blockingReasons.Add("paper validation source is not configured");
        }
        else
        {
            paper = await source
                .GetAsync(request.PaperRunSessionId, Math.Clamp(request.Limit, 1, 10000), cancellationToken)
                .ConfigureAwait(false);
            if (paper is null)
            {
                blockingReasons.Add("paper run report or promotion checklist was not found");
            }
        }

        if (paper is not null)
        {
            var observed = new OpportunityObservedGateMetrics(
                Math.Max(request.SampleMetrics.EvaluableSampleCount, paper.DecisionCount),
                Math.Max(request.SampleMetrics.ObservationDays, paper.ObservationDays),
                paper.NetPnl,
                paper.AdverseSlippage,
                request.PredictedEdge,
                request.SampleMetrics.ScoredBrier,
                request.SampleMetrics.MarketImpliedBrier,
                request.SampleMetrics.SourceDriftScore,
                request.SampleMetrics.CalibrationDriftScore,
                Math.Max(request.SampleMetrics.CriticalRiskEventCount, paper.CriticalRiskEventCount),
                paper.CanConsiderLive ? 0 : 1);
            blockingReasons.AddRange(EvaluateObservedMetrics(
                observed,
                thresholds,
                requirePositivePnl: true,
                requireSlippageBudget: true));
            if (!paper.CanConsiderLive)
            {
                blockingReasons.Add("strategy paper promotion checklist did not pass");
            }

            if (!paper.LiveArmingUnchanged)
            {
                blockingReasons.Add("paper validation must not arm or mutate Live trading state");
            }
        }
        else
        {
            AddSampleMetricFailures(blockingReasons, request.SampleMetrics, thresholds);
        }

        var evidenceIds = paper is null
            ? new[] { request.PaperRunSessionId }
            : paper.EvidenceIds.Concat([request.PaperRunSessionId]).ToArray();
        var run = CreateSucceededRun(
            request.HypothesisId,
            OpportunityEvaluationKind.Paper,
            request.RunVersion,
            request.MarketTapeSliceId,
            new
            {
                kind = OpportunityEvaluationKind.Paper,
                request.HypothesisId,
                request.PaperRunSessionId,
                request.SampleMetrics,
                request.PredictedEdge,
                paper
            },
            BuildMetricsJson("paper", thresholds, blockingReasons, new
            {
                request.PaperRunSessionId,
                request.SampleMetrics,
                request.PredictedEdge,
                paper
            }));

        return await PersistEvaluationAsync(
                run,
                request.HypothesisId,
                OpportunityPromotionGateKind.Paper,
                "opportunity-validation-gates/paper-v1",
                blockingReasons,
                run.ResultJson,
                evidenceIds,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<OpportunityGateEvaluationResult> EvaluateOperationalGateAsync(
        OpportunityOperationalGateRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateHypothesisId(request.HypothesisId);
        if (request.GateKind is not (OpportunityPromotionGateKind.ExecutionQuality
            or OpportunityPromotionGateKind.Risk
            or OpportunityPromotionGateKind.Compliance))
        {
            throw new ArgumentException(
                "Operational gate kind must be ExecutionQuality, Risk, or Compliance.",
                nameof(request));
        }

        var thresholds = request.Thresholds ?? new OpportunityValidationThresholds();
        var blockingReasons = request.GateKind switch
        {
            OpportunityPromotionGateKind.ExecutionQuality => EvaluateObservedMetrics(
                request.Metrics,
                thresholds,
                requirePositivePnl: false,
                requireSlippageBudget: true),
            OpportunityPromotionGateKind.Risk => EvaluateRiskGate(request.Metrics),
            OpportunityPromotionGateKind.Compliance => EvaluateComplianceGate(request),
            _ => []
        };
        var liveArmingStatus = await TryGetLiveArmingStatusAsync(cancellationToken).ConfigureAwait(false);
        var run = CreateSucceededRun(
            request.HypothesisId,
            OpportunityEvaluationKind.Promotion,
            request.RunVersion,
            request.MarketTapeSliceId,
            new
            {
                kind = request.GateKind,
                request.HypothesisId,
                request.Metrics,
                liveArmingStatus
            },
            BuildMetricsJson("operational", thresholds, blockingReasons, new
            {
                request.GateKind,
                request.Metrics,
                request.Explanation,
                liveArmingStatus
            }));

        return await PersistEvaluationAsync(
                run,
                request.HypothesisId,
                request.GateKind,
                $"opportunity-validation-gates/{request.GateKind.ToString().ToLowerInvariant()}-v1",
                blockingReasons,
                run.ResultJson,
                request.EvidenceIds,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<OpportunityLiveEligibilityResult> TryMarkLiveEligibleAsync(
        OpportunityLiveEligibilityRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateHypothesisId(request.HypothesisId);
        if (request.ActivePolicyId == Guid.Empty)
        {
            throw new ArgumentException("ActivePolicyId cannot be empty.", nameof(request));
        }

        var hypothesis = await _repository
            .GetHypothesisAsync(request.HypothesisId, cancellationToken)
            .ConfigureAwait(false);
        if (hypothesis is null)
        {
            return new OpportunityLiveEligibilityResult(
                false,
                null,
                null,
                [],
                ["hypothesis was not found"]);
        }

        var gates = await _repository
            .ListPromotionGatesAsync(request.HypothesisId, cancellationToken)
            .ConfigureAwait(false);
        var latestRequiredGates = LatestRequiredGates(gates);
        var blockingReasons = RequiredGateBlockingReasons(latestRequiredGates);
        if (blockingReasons.Count > 0)
        {
            return new OpportunityLiveEligibilityResult(
                false,
                OpportunityV2Mapper.ToDto(hypothesis),
                null,
                latestRequiredGates.Select(OpportunityV2Mapper.ToDto).ToList(),
                blockingReasons);
        }

        try
        {
            var transition = hypothesis.MarkLiveEligible(
                latestRequiredGates,
                request.ActivePolicyId,
                string.IsNullOrWhiteSpace(request.Actor) ? "opportunity-validation-gates" : request.Actor,
                string.IsNullOrWhiteSpace(request.Reason) ? "all required validation gates passed" : request.Reason,
                request.EvidenceIds,
                DateTimeOffset.UtcNow);
            await _repository
                .UpdateHypothesisWithTransitionAsync(hypothesis, transition, cancellationToken)
                .ConfigureAwait(false);
            return new OpportunityLiveEligibilityResult(
                true,
                OpportunityV2Mapper.ToDto(hypothesis),
                OpportunityV2Mapper.ToDto(transition),
                latestRequiredGates.Select(OpportunityV2Mapper.ToDto).ToList(),
                []);
        }
        catch (OpportunityLifecycleException ex)
        {
            return new OpportunityLiveEligibilityResult(
                false,
                OpportunityV2Mapper.ToDto(hypothesis),
                null,
                latestRequiredGates.Select(OpportunityV2Mapper.ToDto).ToList(),
                [ex.Message]);
        }
    }

    private async Task<OpportunityGateEvaluationResult> PersistEvaluationAsync(
        OpportunityEvaluationRun run,
        Guid hypothesisId,
        OpportunityPromotionGateKind gateKind,
        string evaluator,
        IReadOnlyList<string> blockingReasons,
        string metricsJson,
        IReadOnlyList<Guid> evidenceIds,
        CancellationToken cancellationToken)
    {
        var passed = blockingReasons.Count == 0;
        var gate = new OpportunityPromotionGate(
            hypothesisId,
            gateKind,
            passed ? OpportunityPromotionGateStatus.Passed : OpportunityPromotionGateStatus.Failed,
            evaluator,
            passed ? $"{gateKind} gate passed." : string.Join("; ", blockingReasons),
            metricsJson,
            SerializeEvidenceIds(evidenceIds),
            DateTimeOffset.UtcNow);
        await _repository
            .AddEvaluationRunAndGateAsync(run, gate, cancellationToken)
            .ConfigureAwait(false);

        return new OpportunityGateEvaluationResult(
            OpportunityV2Mapper.ToDto(run),
            OpportunityV2Mapper.ToDto(gate),
            passed,
            blockingReasons,
            metricsJson);
    }

    private static OpportunityEvaluationRun CreateSucceededRun(
        Guid hypothesisId,
        OpportunityEvaluationKind kind,
        string runVersion,
        string marketTapeSliceId,
        object seedInput,
        string resultJson)
    {
        var now = DateTimeOffset.UtcNow;
        var run = new OpportunityEvaluationRun(
            hypothesisId,
            kind,
            runVersion,
            marketTapeSliceId,
            BuildStableSeed(seedInput),
            now);
        run.MarkSucceeded(resultJson, now);
        return run;
    }

    private static List<string> EvaluateObservedMetrics(
        OpportunityObservedGateMetrics metrics,
        OpportunityValidationThresholds thresholds,
        bool requirePositivePnl,
        bool requireSlippageBudget)
    {
        var blockingReasons = new List<string>();
        AddSampleMetricFailures(
            blockingReasons,
            new OpportunityValidationSampleMetrics(
                metrics.EvaluableSampleCount,
                metrics.ObservationDays,
                metrics.ScoredBrier,
                metrics.MarketImpliedBrier,
                metrics.SourceDriftScore,
                metrics.CalibrationDriftScore,
                metrics.CriticalRiskEventCount),
            thresholds);
        if (metrics.BlockingIssueCount > 0)
        {
            blockingReasons.Add($"blocking issue count must be 0; actual={metrics.BlockingIssueCount}");
        }

        if (requirePositivePnl && metrics.NetPnl <= 0m)
        {
            blockingReasons.Add("net paper PnL must be greater than 0");
        }

        if (requireSlippageBudget)
        {
            if (metrics.PredictedEdge <= 0m)
            {
                blockingReasons.Add("predicted edge must be positive before evaluating adverse slippage budget");
            }
            else
            {
                var slippageRatio = metrics.AdverseSlippage <= 0m
                    ? 0m
                    : metrics.AdverseSlippage / metrics.PredictedEdge;
                if (slippageRatio > thresholds.MaxAdverseSlippageToPredictedEdgeRatio)
                {
                    blockingReasons.Add(
                        $"adverse slippage consumed {slippageRatio:P2} of predicted edge; max={thresholds.MaxAdverseSlippageToPredictedEdgeRatio:P2}");
                }
            }
        }

        return blockingReasons;
    }

    private static List<string> EvaluateRiskGate(OpportunityObservedGateMetrics metrics)
    {
        var blockingReasons = new List<string>();
        if (metrics.CriticalRiskEventCount > 0)
        {
            blockingReasons.Add($"critical risk event count must be 0; actual={metrics.CriticalRiskEventCount}");
        }

        if (metrics.BlockingIssueCount > 0)
        {
            blockingReasons.Add($"risk blocking issue count must be 0; actual={metrics.BlockingIssueCount}");
        }

        return blockingReasons;
    }

    private static List<string> EvaluateComplianceGate(OpportunityOperationalGateRequest request)
    {
        var blockingReasons = new List<string>();
        if (request.Metrics.BlockingIssueCount > 0)
        {
            blockingReasons.Add($"compliance blocking issue count must be 0; actual={request.Metrics.BlockingIssueCount}");
        }

        if (request.EvidenceIds.Count == 0)
        {
            blockingReasons.Add("compliance evidence ids are required");
        }

        if (string.IsNullOrWhiteSpace(request.Explanation))
        {
            blockingReasons.Add("compliance explanation is required");
        }

        return blockingReasons;
    }

    private static void AddSampleMetricFailures(
        ICollection<string> blockingReasons,
        OpportunityValidationSampleMetrics metrics,
        OpportunityValidationThresholds thresholds)
    {
        if (metrics.EvaluableSampleCount < thresholds.MinEvaluableSamples
            && metrics.ObservationDays < thresholds.MinObservationDays)
        {
            blockingReasons.Add(
                $"requires at least {thresholds.MinEvaluableSamples} evaluable samples or {thresholds.MinObservationDays} observation days");
        }

        if (metrics.ScoredBrier >= metrics.MarketImpliedBrier)
        {
            blockingReasons.Add("scored Brier must be better than market-implied baseline");
        }

        if (metrics.CriticalRiskEventCount > 0)
        {
            blockingReasons.Add($"critical risk event count must be 0; actual={metrics.CriticalRiskEventCount}");
        }

        if (metrics.SourceDriftScore > thresholds.MaxSourceDriftScore)
        {
            blockingReasons.Add(
                $"source drift {metrics.SourceDriftScore:0.####} exceeds max {thresholds.MaxSourceDriftScore:0.####}");
        }

        if (metrics.CalibrationDriftScore > thresholds.MaxCalibrationDriftScore)
        {
            blockingReasons.Add(
                $"calibration drift {metrics.CalibrationDriftScore:0.####} exceeds max {thresholds.MaxCalibrationDriftScore:0.####}");
        }
    }

    private async Task<object?> TryGetLiveArmingStatusAsync(CancellationToken cancellationToken)
    {
        var service = _liveArmingServices.FirstOrDefault();
        if (service is null)
        {
            return null;
        }

        try
        {
            return await service.GetStatusAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new { unavailable = true, ex.Message };
        }
    }

    private static IReadOnlyList<OpportunityPromotionGate> LatestRequiredGates(
        IReadOnlyList<OpportunityPromotionGate> gates)
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

    private static IReadOnlyList<string> RequiredGateBlockingReasons(
        IReadOnlyList<OpportunityPromotionGate> latestRequiredGates)
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

    private static string BuildMetricsJson(
        string gate,
        OpportunityValidationThresholds thresholds,
        IReadOnlyList<string> blockingReasons,
        object payload)
        => JsonSerializer.Serialize(
            new
            {
                gate,
                thresholds,
                passed = blockingReasons.Count == 0,
                blockingReasons,
                payload
            },
            JsonOptions);

    private static string SerializeEvidenceIds(IReadOnlyList<Guid> evidenceIds)
        => JsonSerializer.Serialize(
            evidenceIds
                .Where(id => id != Guid.Empty)
                .Distinct()
                .ToArray(),
            JsonOptions);

    private static string BuildStableSeed(object seedInput)
    {
        var json = JsonSerializer.Serialize(seedInput, JsonOptions);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
    }

    private static void ValidateHypothesisId(Guid hypothesisId)
    {
        if (hypothesisId == Guid.Empty)
        {
            throw new ArgumentException("HypothesisId cannot be empty.", nameof(hypothesisId));
        }
    }
}
