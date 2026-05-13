using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Autotrade.MarketData.Application.Contract.Snapshots;
using Autotrade.OpportunityDiscovery.Application.Contract;
using Autotrade.OpportunityDiscovery.Domain.Exceptions;
using Autotrade.OpportunityDiscovery.Domain.Shared.Enums;

namespace Autotrade.OpportunityDiscovery.Application;

public sealed class OpportunityOperatorService : IOpportunityOperatorService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly Regex SensitiveAssignmentPattern = new(
        "(?i)(api[_-]?key|private[_-]?key|secret|password|authorization|bearer|mnemonic|seed[_-]?phrase)\\s*[:=]\\s*[^\\s,;}]+",
        RegexOptions.Compiled);

    private readonly IOpportunityV2Repository _repository;
    private readonly IEvidenceSnapshotRepository _evidenceSnapshots;
    private readonly ISourceProfileRepository _sourceProfiles;
    private readonly IOpportunityValidationGateService _validationGateService;
    private readonly IOpportunityLiveAllocationService _liveAllocationService;
    private readonly IExecutableOpportunityPolicyFeed _executablePolicyFeed;
    private readonly IMarketDataSnapshotReader? _marketDataSnapshotReader;

    public OpportunityOperatorService(
        IOpportunityV2Repository repository,
        IEvidenceSnapshotRepository evidenceSnapshots,
        ISourceProfileRepository sourceProfiles,
        IOpportunityValidationGateService validationGateService,
        IOpportunityLiveAllocationService liveAllocationService,
        IExecutableOpportunityPolicyFeed executablePolicyFeed,
        IEnumerable<IMarketDataSnapshotReader> marketDataSnapshotReaders)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _evidenceSnapshots = evidenceSnapshots ?? throw new ArgumentNullException(nameof(evidenceSnapshots));
        _sourceProfiles = sourceProfiles ?? throw new ArgumentNullException(nameof(sourceProfiles));
        _validationGateService = validationGateService ?? throw new ArgumentNullException(nameof(validationGateService));
        _liveAllocationService = liveAllocationService ?? throw new ArgumentNullException(nameof(liveAllocationService));
        _executablePolicyFeed = executablePolicyFeed ?? throw new ArgumentNullException(nameof(executablePolicyFeed));
        _marketDataSnapshotReader = marketDataSnapshotReaders?.FirstOrDefault();
    }

    public async Task<OpportunityScoreStatusResponse> GetScoreAsync(
        Guid opportunityId,
        CancellationToken cancellationToken = default)
    {
        var hypothesis = await _repository.GetHypothesisAsync(opportunityId, cancellationToken).ConfigureAwait(false);
        if (hypothesis is null)
        {
            return new OpportunityScoreStatusResponse(opportunityId, null, null, ["opportunity was not found"]);
        }

        var score = await _repository.GetLatestScoreAsync(opportunityId, cancellationToken).ConfigureAwait(false);
        return new OpportunityScoreStatusResponse(
            opportunityId,
            OpportunityV2Mapper.ToDto(hypothesis),
            score is null ? null : Redact(OpportunityV2Mapper.ToDto(score)),
            score is null ? ["no score has been recorded"] : []);
    }

    public async Task<OpportunityReplayStatusResponse> GetReplayAsync(
        Guid opportunityId,
        CancellationToken cancellationToken = default)
    {
        var hypothesis = await _repository.GetHypothesisAsync(opportunityId, cancellationToken).ConfigureAwait(false);
        if (hypothesis is null)
        {
            return new OpportunityReplayStatusResponse(opportunityId, null, [], [], null, null, ["opportunity was not found"]);
        }

        var runs = await _repository.ListEvaluationRunsAsync(opportunityId, cancellationToken).ConfigureAwait(false);
        var gates = await _repository.ListPromotionGatesAsync(opportunityId, cancellationToken).ConfigureAwait(false);
        var latestBacktest = runs
            .Where(run => run.EvaluationKind == OpportunityEvaluationKind.Backtest)
            .OrderByDescending(run => run.UpdatedAtUtc)
            .FirstOrDefault();

        return new OpportunityReplayStatusResponse(
            opportunityId,
            OpportunityV2Mapper.ToDto(hypothesis),
            runs.Select(run => Redact(OpportunityV2Mapper.ToDto(run))).ToList(),
            gates.Select(gate => Redact(OpportunityV2Mapper.ToDto(gate))).ToList(),
            latestBacktest?.ReplaySeed ?? hypothesis.ReplaySeed,
            latestBacktest?.MarketTapeSliceId ?? hypothesis.MarketTapeSliceId,
            runs.Count == 0 ? ["no replay or validation runs have been recorded"] : []);
    }

    public async Task<OpportunityPromoteResponse> PromoteAsync(
        OpportunityPromoteRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var hypothesis = await _repository.GetHypothesisAsync(request.OpportunityId, cancellationToken).ConfigureAwait(false);
        if (hypothesis is null)
        {
            return new OpportunityPromoteResponse(request.OpportunityId, false, null, ["opportunity was not found"]);
        }

        var gates = await _repository.ListPromotionGatesAsync(request.OpportunityId, cancellationToken).ConfigureAwait(false);
        if (hypothesis.Status is OpportunityHypothesisStatus.LiveEligible or OpportunityHypothesisStatus.LivePublished)
        {
            return new OpportunityPromoteResponse(
                request.OpportunityId,
                true,
                new OpportunityLiveEligibilityResult(
                    true,
                    OpportunityV2Mapper.ToDto(hypothesis),
                    null,
                    gates.Select(OpportunityV2Mapper.ToDto).ToList(),
                    []),
                []);
        }

        var now = DateTimeOffset.UtcNow;
        var policyId = hypothesis.ActivePolicyId
            ?? (await _repository.GetActiveExecutablePolicyForHypothesisAsync(request.OpportunityId, now, cancellationToken)
                .ConfigureAwait(false))?.Id;
        if (policyId is null || policyId.Value == Guid.Empty)
        {
            return new OpportunityPromoteResponse(request.OpportunityId, false, null, ["no active executable policy is available"]);
        }

        var result = await _validationGateService.TryMarkLiveEligibleAsync(
                new OpportunityLiveEligibilityRequest(
                    request.OpportunityId,
                    policyId.Value,
                    ResolveActor(request.Actor),
                    string.IsNullOrWhiteSpace(request.Reason) ? "operator promotion" : RedactText(request.Reason),
                    []),
                cancellationToken)
            .ConfigureAwait(false);

        return new OpportunityPromoteResponse(request.OpportunityId, result.LiveEligible, result, result.BlockingReasons);
    }

    public async Task<OpportunityLiveStatusResponse> GetLiveStatusAsync(
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var policies = await _executablePolicyFeed.GetExecutableAsync(500, cancellationToken).ConfigureAwait(false);
        var allocations = await _repository.ListActiveLiveAllocationsAsync(now, cancellationToken).ConfigureAwait(false);
        var hypotheses = await _repository.ListLiveHypothesesAsync(cancellationToken).ConfigureAwait(false);
        return new OpportunityLiveStatusResponse(
            now,
            policies,
            allocations.Select(OpportunityV2Mapper.ToDto).ToList(),
            hypotheses.Select(OpportunityV2Mapper.ToDto).ToList(),
            []);
    }

    public async Task<OpportunityOperatorSuspendResponse> SuspendAsync(
        OpportunityOperatorSuspendRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.OpportunityId == Guid.Empty)
        {
            return new OpportunityOperatorSuspendResponse(request.OpportunityId, false, null, null, null, ["opportunity id is required"]);
        }

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return new OpportunityOperatorSuspendResponse(request.OpportunityId, false, null, null, null, ["suspension reason is required"]);
        }

        var now = DateTimeOffset.UtcNow;
        var hypothesis = await _repository.GetHypothesisAsync(request.OpportunityId, cancellationToken).ConfigureAwait(false);
        if (hypothesis is null)
        {
            return new OpportunityOperatorSuspendResponse(request.OpportunityId, false, null, null, null, ["opportunity was not found"]);
        }

        var policy = hypothesis.ActivePolicyId.HasValue
            ? await _repository.GetExecutablePolicyAsync(hypothesis.ActivePolicyId.Value, cancellationToken).ConfigureAwait(false)
            : await _repository.GetActiveExecutablePolicyForHypothesisAsync(request.OpportunityId, now, cancellationToken)
                .ConfigureAwait(false);
        if (policy is null)
        {
            return new OpportunityOperatorSuspendResponse(
                request.OpportunityId,
                false,
                OpportunityV2Mapper.ToDto(hypothesis),
                null,
                null,
                ["no executable policy is available to suspend"]);
        }

        var allocation = hypothesis.ActiveLiveAllocationId.HasValue
            ? await _repository.GetLiveAllocationAsync(hypothesis.ActiveLiveAllocationId.Value, cancellationToken).ConfigureAwait(false)
            : await _repository.GetActiveLiveAllocationForHypothesisAsync(request.OpportunityId, now, cancellationToken)
                .ConfigureAwait(false);

        try
        {
            var result = await _liveAllocationService.SuspendIfKillCriteriaAsync(
                    new OpportunitySuspensionRequest(
                        request.OpportunityId,
                        policy.Id,
                        allocation?.Id,
                        request.StrategyId,
                        string.IsNullOrWhiteSpace(request.MarketId) ? policy.MarketId : request.MarketId,
                        ResolveActor(request.Actor),
                        RedactText(request.Reason),
                        new OpportunityKillCriteriaMetrics(
                            RealizedEdge: 0m,
                            PredictedEdge: Math.Max(policy.Edge, 0.000001m),
                            AdverseSlippage: 0m,
                            FillRate: 1m,
                            DrawdownUsdc: 0m,
                            SourceDriftScore: 0m,
                            CalibrationDriftScore: 0m,
                            OrderBookAgeSeconds: 0,
                            RiskEventCount: 0,
                            ComplianceEventCount: 1),
                        []),
                    cancellationToken)
                .ConfigureAwait(false);

            return new OpportunityOperatorSuspendResponse(
                request.OpportunityId,
                result.Suspended,
                result.Hypothesis,
                result.Allocation,
                result.Transition,
                result.Suspended ? result.KillReasons : ["suspension criteria did not trigger"]);
        }
        catch (OpportunityLifecycleException ex)
        {
            return new OpportunityOperatorSuspendResponse(
                request.OpportunityId,
                false,
                OpportunityV2Mapper.ToDto(hypothesis),
                allocation is null ? null : OpportunityV2Mapper.ToDto(allocation),
                null,
                [ex.Message]);
        }
    }

    public async Task<OpportunityExplainResponse> ExplainAsync(
        Guid opportunityId,
        DateTimeOffset? asOfUtc = null,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var hypothesis = await _repository.GetHypothesisAsync(opportunityId, cancellationToken).ConfigureAwait(false);
        if (hypothesis is null)
        {
            return new OpportunityExplainResponse(
                opportunityId,
                now,
                true,
                null,
                null,
                [],
                [],
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                [],
                [],
                null,
                null,
                [],
                [],
                ["opportunity was not found"]);
        }

        var resolvedAsOf = asOfUtc ?? now;
        var evidenceBundle = await _evidenceSnapshots
            .GetForOpportunityAsOfAsync(opportunityId, resolvedAsOf, cancellationToken)
            .ConfigureAwait(false);
        var evidence = evidenceBundle is null
            ? null
            : Redact(new OpportunityEvidenceExplainDto(
                opportunityId,
                resolvedAsOf,
                SourceRegistryMapper.ToDto(evidenceBundle),
                evidenceBundle.Snapshot.LiveGateStatus == EvidenceSnapshotLiveGateStatus.Eligible,
                ParseReasons(evidenceBundle.Snapshot.LiveGateReasonsJson).Select(RedactText).ToList()));

        var sourceKeys = evidence?.Snapshot.Citations
            .Select(citation => citation.SourceKey)
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? [];
        var sourceProfiles = sourceKeys.Count == 0
            ? []
            : (await _sourceProfiles.GetCurrentByKeysAsync(sourceKeys, cancellationToken).ConfigureAwait(false))
                .Values
                .OrderBy(profile => profile.SourceKey)
                .Select(SourceRegistryMapper.ToDto)
                .Select(Redact)
                .ToList();
        var gates = await _repository.ListPromotionGatesAsync(opportunityId, cancellationToken).ConfigureAwait(false);
        var runs = await _repository.ListEvaluationRunsAsync(opportunityId, cancellationToken).ConfigureAwait(false);
        var featureSnapshot = await _repository.GetLatestFeatureSnapshotAsync(opportunityId, cancellationToken)
            .ConfigureAwait(false);
        var score = await _repository.GetLatestScoreAsync(opportunityId, cancellationToken).ConfigureAwait(false);
        var allocation = hypothesis.ActiveLiveAllocationId.HasValue
            ? await _repository.GetLiveAllocationAsync(hypothesis.ActiveLiveAllocationId.Value, cancellationToken)
                .ConfigureAwait(false)
            : await _repository.GetActiveLiveAllocationForHypothesisAsync(opportunityId, now, cancellationToken)
                .ConfigureAwait(false);
        var executablePolicy = (await _executablePolicyFeed.GetExecutableAsync(500, cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(policy => policy.HypothesisId == opportunityId);
        var currentSnapshot = _marketDataSnapshotReader?.GetSnapshot(
            hypothesis.MarketId,
            TimeSpan.FromMinutes(5),
            TimeSpan.FromSeconds(10),
            depthLevels: 5);
        var blockingReasons = new List<string>();
        if (evidence is null)
        {
            blockingReasons.Add("no point-in-time evidence snapshot was found");
        }

        if (score is null)
        {
            blockingReasons.Add("no score was found");
        }

        if (executablePolicy is null)
        {
            blockingReasons.Add("no currently executable live policy was found");
        }

        return new OpportunityExplainResponse(
            opportunityId,
            now,
            true,
            OpportunityV2Mapper.ToDto(hypothesis),
            evidence,
            sourceProfiles,
            evidence?.Snapshot.Conflicts ?? [],
            hypothesis.MarketTapeSliceId,
            currentSnapshot,
            RedactText(hypothesis.PromptVersion),
            RedactText(hypothesis.ModelVersion),
            RedactText(score?.ScoreVersion ?? hypothesis.ScoreVersion),
            featureSnapshot is null ? null : Redact(OpportunityV2Mapper.ToDto(featureSnapshot)),
            score is null ? null : Redact(OpportunityV2Mapper.ToDto(score)),
            score is null ? null : RedactJson(score.ComponentsJson),
            score is null ? null : score.NetEdge * score.ExecutableCapacity,
            score?.ExecutableCapacity,
            gates.Select(gate => Redact(OpportunityV2Mapper.ToDto(gate))).ToList(),
            runs.Select(run => Redact(OpportunityV2Mapper.ToDto(run))).ToList(),
            executablePolicy,
            allocation is null ? null : OpportunityV2Mapper.ToDto(allocation),
            ExtractGateReasons(gates, OpportunityPromotionGateKind.Risk),
            ExtractGateReasons(gates, OpportunityPromotionGateKind.Compliance),
            blockingReasons);
    }

    private static string ResolveActor(string actor)
        => string.IsNullOrWhiteSpace(actor) ? "operator" : RedactText(actor.Trim());

    private static IReadOnlyList<string> ExtractGateReasons(
        IReadOnlyList<Domain.Entities.OpportunityPromotionGate> gates,
        OpportunityPromotionGateKind kind)
        => gates
            .Where(gate => gate.GateKind == kind)
            .OrderByDescending(gate => gate.EvaluatedAtUtc)
            .Select(gate => RedactText(gate.Reason))
            .ToList();

    private static IReadOnlyList<string> ParseReasons(string reasonsJson)
    {
        if (string.IsNullOrWhiteSpace(reasonsJson))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<IReadOnlyList<string>>(reasonsJson, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [RedactText(reasonsJson)];
        }
    }

    private static OpportunityEvidenceExplainDto Redact(OpportunityEvidenceExplainDto value)
        => value with
        {
            Snapshot = Redact(value.Snapshot),
            BlockingReasons = value.BlockingReasons.Select(RedactText).ToList()
        };

    private static EvidenceSnapshotDto Redact(EvidenceSnapshotDto value)
        => value with
        {
            SummaryJson = RedactJson(value.SummaryJson),
            Citations = value.Citations.Select(Redact).ToList(),
            Conflicts = value.Conflicts.Select(Redact).ToList(),
            OfficialConfirmations = value.OfficialConfirmations.Select(Redact).ToList()
        };

    private static EvidenceCitationDto Redact(EvidenceCitationDto value)
        => value with
        {
            Url = RedactText(value.Url),
            Title = RedactText(value.Title),
            ClaimJson = RedactJson(value.ClaimJson)
        };

    private static EvidenceConflictDto Redact(EvidenceConflictDto value)
        => value with
        {
            Description = RedactText(value.Description),
            SourceKeysJson = RedactJson(value.SourceKeysJson)
        };

    private static OfficialConfirmationDto Redact(OfficialConfirmationDto value)
        => value with
        {
            Claim = RedactText(value.Claim),
            Url = RedactText(value.Url),
            RawJson = RedactJson(value.RawJson)
        };

    private static SourceProfileDto Redact(SourceProfileDto value)
        => value with
        {
            CoveredCategoriesJson = RedactJson(value.CoveredCategoriesJson),
            ChangeReason = RedactText(value.ChangeReason)
        };

    private static OpportunityFeatureSnapshotDto Redact(OpportunityFeatureSnapshotDto value)
        => value with { FeaturesJson = RedactJson(value.FeaturesJson) };

    private static OpportunityScoreDto Redact(OpportunityScoreDto value)
        => value with
        {
            CalibrationBucket = RedactText(value.CalibrationBucket),
            ComponentsJson = RedactJson(value.ComponentsJson)
        };

    private static OpportunityEvaluationRunDto Redact(OpportunityEvaluationRunDto value)
        => value with
        {
            ResultJson = RedactJson(value.ResultJson),
            ErrorMessage = value.ErrorMessage is null ? null : RedactText(value.ErrorMessage)
        };

    private static OpportunityPromotionGateDto Redact(OpportunityPromotionGateDto value)
        => value with
        {
            Reason = RedactText(value.Reason),
            MetricsJson = RedactJson(value.MetricsJson),
            EvidenceIdsJson = RedactJson(value.EvidenceIdsJson)
        };

    private static string RedactText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return SensitiveAssignmentPattern.Replace(value, "$1=[REDACTED]");
    }

    private static string RedactJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return "{}";
        }

        try
        {
            var node = JsonNode.Parse(json);
            RedactNode(node);
            return node?.ToJsonString(JsonOptions) ?? "{}";
        }
        catch (JsonException)
        {
            return RedactText(json);
        }
    }

    private static void RedactNode(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var item in obj.ToList())
                {
                    if (IsSensitiveKey(item.Key))
                    {
                        obj[item.Key] = "[REDACTED]";
                    }
                    else
                    {
                        RedactNode(item.Value);
                    }
                }

                break;
            case JsonArray array:
                foreach (var item in array)
                {
                    RedactNode(item);
                }

                break;
        }
    }

    private static bool IsSensitiveKey(string key)
    {
        var normalized = key.Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();
        return normalized.Contains("apikey", StringComparison.Ordinal)
            || normalized.Contains("privatekey", StringComparison.Ordinal)
            || normalized.Contains("secret", StringComparison.Ordinal)
            || normalized.Contains("password", StringComparison.Ordinal)
            || normalized.Contains("authorization", StringComparison.Ordinal)
            || normalized.Contains("bearer", StringComparison.Ordinal)
            || normalized.Contains("mnemonic", StringComparison.Ordinal)
            || normalized.Contains("seedphrase", StringComparison.Ordinal)
            || normalized is "accesstoken" or "refreshtoken" or "idtoken";
    }
}
