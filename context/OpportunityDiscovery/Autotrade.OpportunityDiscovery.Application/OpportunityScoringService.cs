using System.Text.Json;
using System.Text.Json.Serialization;
using Autotrade.OpportunityDiscovery.Application.Contract;
using Autotrade.OpportunityDiscovery.Domain.Entities;
using Autotrade.OpportunityDiscovery.Domain.Shared.Enums;

namespace Autotrade.OpportunityDiscovery.Application;

public sealed class OpportunityScoringService : IOpportunityScoringService
{
    public const string DefaultScoreVersion = "deterministic-net-edge-v1";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly IOpportunityV2Repository _repository;

    public OpportunityScoringService(IOpportunityV2Repository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<OpportunityScoringResult> ScoreAsync(
        OpportunityScoringRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.HypothesisId == Guid.Empty)
        {
            throw new ArgumentException("HypothesisId cannot be empty.", nameof(request));
        }

        if (request.EvidenceSnapshotId == Guid.Empty)
        {
            throw new ArgumentException("EvidenceSnapshotId cannot be empty.", nameof(request));
        }

        var createdAtUtc = request.CreatedAtUtc == default ? DateTimeOffset.UtcNow : request.CreatedAtUtc;
        var scoreVersion = string.IsNullOrWhiteSpace(request.ScoreVersion)
            ? DefaultScoreVersion
            : request.ScoreVersion.Trim();
        var normalized = Normalize(request.Features);
        var calculated = Calculate(normalized);
        var blockingReasons = BuildBlockingReasons(calculated.NetEdge, normalized.ExecutableCapacity);
        var canPromote = blockingReasons.Count == 0;
        var featuresJson = BuildFeaturesJson(request, normalized, calculated);
        var breakdownJson = BuildBreakdownJson(scoreVersion, normalized, calculated, canPromote, blockingReasons);

        var snapshot = new OpportunityFeatureSnapshot(
            request.HypothesisId,
            request.EvidenceSnapshotId,
            request.MarketTapeSliceId,
            request.FeatureVersion,
            featuresJson,
            createdAtUtc);
        var score = new OpportunityScore(
            request.HypothesisId,
            snapshot.Id,
            scoreVersion,
            normalized.LlmFairProbability,
            calculated.FairProbability,
            calculated.Confidence,
            calculated.Edge,
            normalized.MarketImpliedProbability,
            normalized.ExecutableEntryPrice,
            normalized.FeeEstimate,
            normalized.SlippageBuffer,
            calculated.NetEdge,
            normalized.ExecutableCapacity,
            canPromote,
            calculated.CalibrationBucket,
            breakdownJson,
            createdAtUtc);

        await _repository
            .AddFeatureSnapshotAndScoreAsync(snapshot, score, cancellationToken)
            .ConfigureAwait(false);

        return new OpportunityScoringResult(
            OpportunityV2Mapper.ToDto(snapshot),
            OpportunityV2Mapper.ToDto(score),
            canPromote,
            blockingReasons,
            breakdownJson);
    }

    private static NormalizedFeatures Normalize(OpportunityFeatureVector features)
    {
        var llmFairProbability = Round4(RequireProbability(features.LlmFairProbability, nameof(features.LlmFairProbability)));
        var evidenceConfidence = Round4(RequireProbability(features.EvidenceConfidence, nameof(features.EvidenceConfidence)));
        var officialConfirmationStrength = Round4(RequireProbability(features.OfficialConfirmationStrength, nameof(features.OfficialConfirmationStrength)));
        var liquidityCapacity = Round4(RequireNonNegative(features.LiquidityCapacity, nameof(features.LiquidityCapacity)));
        var spread = Round4(RequireProbability(features.Spread, nameof(features.Spread)));
        var marketImpact = Round4(RequireProbability(features.MarketImpact, nameof(features.MarketImpact)));
        var timeHalfLife = Round4(RequireProbability(features.TimeHalfLife, nameof(features.TimeHalfLife)));
        var sourceConflictPenalty = Round4(RequireProbability(features.SourceConflictPenalty, nameof(features.SourceConflictPenalty)));
        var resolutionRisk = Round4(RequireProbability(features.ResolutionRisk, nameof(features.ResolutionRisk)));
        var executionFreshness = Round4(RequireProbability(features.ExecutionFreshness, nameof(features.ExecutionFreshness)));
        var marketClosureAmbiguity = Round4(RequireProbability(features.MarketClosureAmbiguity, nameof(features.MarketClosureAmbiguity)));
        var marketImpliedProbability = Round4(RequireProbability(features.MarketImpliedProbability, nameof(features.MarketImpliedProbability)));
        var executableEntryPrice = Round4(RequireProbability(features.ExecutableEntryPrice, nameof(features.ExecutableEntryPrice)));
        var feeEstimate = Round4(RequireNonNegative(features.FeeEstimate, nameof(features.FeeEstimate)));
        var slippageBuffer = Round4(RequireNonNegative(features.SlippageBuffer, nameof(features.SlippageBuffer)));
        var executableCapacity = Round4(RequireNonNegative(features.ExecutableCapacity, nameof(features.ExecutableCapacity)));
        var liquidityQuality = Round4(ToLiquidityQuality(liquidityCapacity));

        return new NormalizedFeatures(
            features.OpportunityType,
            llmFairProbability,
            evidenceConfidence,
            officialConfirmationStrength,
            liquidityCapacity,
            liquidityQuality,
            spread,
            marketImpact,
            timeHalfLife,
            sourceConflictPenalty,
            resolutionRisk,
            executionFreshness,
            marketClosureAmbiguity,
            marketImpliedProbability,
            executableEntryPrice,
            feeEstimate,
            slippageBuffer,
            executableCapacity);
    }

    private static CalculatedScore Calculate(NormalizedFeatures features)
    {
        var anchoredProbability = Round4(
            (features.LlmFairProbability * 0.70m)
            + (features.MarketImpliedProbability * 0.30m));
        var evidenceAdjustment = Round4((features.EvidenceConfidence - 0.50m) * 0.06m);
        var officialAdjustment = Round4((features.OfficialConfirmationStrength - 0.50m) * 0.05m);
        var freshnessAdjustment = Round4((features.ExecutionFreshness - 0.50m) * 0.03m);
        var liquidityAdjustment = Round4((features.LiquidityQuality - 0.50m) * 0.02m);
        var typePriorAdjustment = OpportunityTypePriorAdjustment(features.OpportunityType);
        var sourceConflictPenalty = Round4(features.SourceConflictPenalty * 0.12m);
        var resolutionRiskPenalty = Round4(features.ResolutionRisk * 0.10m);
        var marketImpactPenalty = Round4(features.MarketImpact * 0.08m);
        var spreadPenalty = Round4(features.Spread * 0.04m);
        var timeDecayPenalty = Round4((1.00m - features.TimeHalfLife) * 0.04m);
        var closureAmbiguityPenalty = Round4(features.MarketClosureAmbiguity * 0.08m);
        var fairProbability = Round4(ClampProbability(
            anchoredProbability
            + evidenceAdjustment
            + officialAdjustment
            + freshnessAdjustment
            + liquidityAdjustment
            + typePriorAdjustment
            - sourceConflictPenalty
            - resolutionRiskPenalty
            - marketImpactPenalty
            - spreadPenalty
            - timeDecayPenalty
            - closureAmbiguityPenalty));
        var confidence = Round4(ClampProbability(
            (features.EvidenceConfidence * 0.25m)
            + (features.OfficialConfirmationStrength * 0.20m)
            + (features.ExecutionFreshness * 0.15m)
            + (features.TimeHalfLife * 0.10m)
            + (features.LiquidityQuality * 0.10m)
            + ((1.00m - features.Spread) * 0.05m)
            + ((1.00m - features.MarketImpact) * 0.05m)
            - (features.SourceConflictPenalty * 0.05m)
            - (features.ResolutionRisk * 0.05m)
            - (features.MarketClosureAmbiguity * 0.05m)));
        var edge = fairProbability - features.MarketImpliedProbability;
        var netEdge = fairProbability
            - features.ExecutableEntryPrice
            - features.FeeEstimate
            - features.SlippageBuffer;

        return new CalculatedScore(
            anchoredProbability,
            evidenceAdjustment,
            officialAdjustment,
            freshnessAdjustment,
            liquidityAdjustment,
            typePriorAdjustment,
            sourceConflictPenalty,
            resolutionRiskPenalty,
            marketImpactPenalty,
            spreadPenalty,
            timeDecayPenalty,
            closureAmbiguityPenalty,
            fairProbability,
            confidence,
            edge,
            netEdge,
            CalibrationBucket(edge));
    }

    private static IReadOnlyList<string> BuildBlockingReasons(decimal netEdge, decimal executableCapacity)
    {
        var reasons = new List<string>();
        if (netEdge <= 0m)
        {
            reasons.Add("positive net edge required: fairProbability - executableEntryPrice - feeEstimate - slippageBuffer <= 0");
        }

        if (executableCapacity <= 0m)
        {
            reasons.Add("executable capacity required: executableCapacity <= 0");
        }

        return reasons;
    }

    private static string BuildFeaturesJson(
        OpportunityScoringRequest request,
        NormalizedFeatures features,
        CalculatedScore calculated)
        => JsonSerializer.Serialize(
            new
            {
                request.HypothesisId,
                request.EvidenceSnapshotId,
                request.MarketTapeSliceId,
                request.FeatureVersion,
                features,
                derived = new
                {
                    calculated.AnchoredProbability,
                    calculated.FairProbability,
                    calculated.Confidence,
                    calculated.Edge,
                    calculated.NetEdge,
                    calculated.CalibrationBucket
                }
            },
            JsonOptions);

    private static string BuildBreakdownJson(
        string scoreVersion,
        NormalizedFeatures features,
        CalculatedScore calculated,
        bool canPromote,
        IReadOnlyList<string> blockingReasons)
        => JsonSerializer.Serialize(
            new
            {
                scoreVersion,
                formula = new
                {
                    fairProbability = "clamp(0.70*llmFairProbability + 0.30*marketImpliedProbability + deterministic feature adjustments - deterministic feature penalties)",
                    edge = "fairProbability - marketImpliedProbability",
                    netEdge = "fairProbability - executableEntryPrice - feeEstimate - slippageBuffer"
                },
                llmUsage = "llmFairProbability is an input feature only; deterministic scoring anchors it to market-implied probability and feature penalties.",
                canPromote,
                blockingReasons,
                features,
                adjustments = new
                {
                    calculated.AnchoredProbability,
                    calculated.EvidenceAdjustment,
                    calculated.OfficialAdjustment,
                    calculated.FreshnessAdjustment,
                    calculated.LiquidityAdjustment,
                    calculated.TypePriorAdjustment,
                    calculated.SourceConflictPenalty,
                    calculated.ResolutionRiskPenalty,
                    calculated.MarketImpactPenalty,
                    calculated.SpreadPenalty,
                    calculated.TimeDecayPenalty,
                    calculated.ClosureAmbiguityPenalty
                },
                result = new
                {
                    calculated.FairProbability,
                    calculated.Confidence,
                    calculated.Edge,
                    calculated.NetEdge,
                    calculated.CalibrationBucket
                }
            },
            JsonOptions);

    private static decimal OpportunityTypePriorAdjustment(OpportunityType type)
        => type switch
        {
            OpportunityType.InformationAsymmetry => 0.0100m,
            OpportunityType.CrossMarketConsistency => 0.0050m,
            OpportunityType.DelayedRepricing => 0.0075m,
            OpportunityType.OrderBookMicrostructure => -0.0050m,
            OpportunityType.NearResolution => -0.0100m,
            OpportunityType.LiquidityMismatch => -0.0025m,
            _ => 0m
        };

    private static decimal ToLiquidityQuality(decimal liquidityCapacity)
        => liquidityCapacity switch
        {
            <= 0m => 0m,
            < 10m => 0.25m,
            < 50m => 0.50m,
            < 250m => 0.75m,
            _ => 1.00m
        };

    private static string CalibrationBucket(decimal edge)
    {
        if (edge >= 0.10m)
        {
            return "over-market:10+";
        }

        if (edge >= 0.05m)
        {
            return "over-market:05-10";
        }

        if (edge >= 0.02m)
        {
            return "over-market:02-05";
        }

        if (edge > -0.02m)
        {
            return "at-market:-02-02";
        }

        if (edge > -0.05m)
        {
            return "under-market:02-05";
        }

        if (edge > -0.10m)
        {
            return "under-market:05-10";
        }

        return "under-market:10+";
    }

    private static decimal RequireProbability(decimal value, string paramName)
    {
        if (value < 0m || value > 1m)
        {
            throw new ArgumentOutOfRangeException(paramName, value, "Value must be in 0..1.");
        }

        return value;
    }

    private static decimal RequireNonNegative(decimal value, string paramName)
    {
        if (value < 0m)
        {
            throw new ArgumentOutOfRangeException(paramName, value, "Value cannot be negative.");
        }

        return value;
    }

    private static decimal ClampProbability(decimal value)
        => Math.Clamp(value, 0m, 1m);

    private static decimal Round4(decimal value)
        => Math.Round(value, 4, MidpointRounding.AwayFromZero);

    private sealed record NormalizedFeatures(
        OpportunityType OpportunityType,
        decimal LlmFairProbability,
        decimal EvidenceConfidence,
        decimal OfficialConfirmationStrength,
        decimal LiquidityCapacity,
        decimal LiquidityQuality,
        decimal Spread,
        decimal MarketImpact,
        decimal TimeHalfLife,
        decimal SourceConflictPenalty,
        decimal ResolutionRisk,
        decimal ExecutionFreshness,
        decimal MarketClosureAmbiguity,
        decimal MarketImpliedProbability,
        decimal ExecutableEntryPrice,
        decimal FeeEstimate,
        decimal SlippageBuffer,
        decimal ExecutableCapacity);

    private sealed record CalculatedScore(
        decimal AnchoredProbability,
        decimal EvidenceAdjustment,
        decimal OfficialAdjustment,
        decimal FreshnessAdjustment,
        decimal LiquidityAdjustment,
        decimal TypePriorAdjustment,
        decimal SourceConflictPenalty,
        decimal ResolutionRiskPenalty,
        decimal MarketImpactPenalty,
        decimal SpreadPenalty,
        decimal TimeDecayPenalty,
        decimal ClosureAmbiguityPenalty,
        decimal FairProbability,
        decimal Confidence,
        decimal Edge,
        decimal NetEdge,
        string CalibrationBucket);
}
