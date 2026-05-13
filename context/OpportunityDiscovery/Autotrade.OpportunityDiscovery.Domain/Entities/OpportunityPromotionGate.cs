using Autotrade.OpportunityDiscovery.Domain.Shared.Enums;
using NetDevPack.Domain;

namespace Autotrade.OpportunityDiscovery.Domain.Entities;

public sealed class OpportunityPromotionGate : Entity, IAggregateRoot
{
    private OpportunityPromotionGate()
    {
        Evaluator = string.Empty;
        Reason = string.Empty;
        MetricsJson = "{}";
        EvidenceIdsJson = "[]";
        EvaluatedAtUtc = DateTimeOffset.UtcNow;
    }

    public OpportunityPromotionGate(
        Guid hypothesisId,
        OpportunityPromotionGateKind gateKind,
        OpportunityPromotionGateStatus status,
        string evaluator,
        string reason,
        string metricsJson,
        string evidenceIdsJson,
        DateTimeOffset evaluatedAtUtc)
    {
        HypothesisId = hypothesisId == Guid.Empty
            ? throw new ArgumentException("HypothesisId cannot be empty.", nameof(hypothesisId))
            : hypothesisId;
        GateKind = gateKind;
        Status = status;
        Evaluator = Required(evaluator, nameof(evaluator), 128);
        Reason = Required(reason, nameof(reason), 2048);
        MetricsJson = string.IsNullOrWhiteSpace(metricsJson) ? "{}" : metricsJson.Trim();
        EvidenceIdsJson = string.IsNullOrWhiteSpace(evidenceIdsJson) ? "[]" : evidenceIdsJson.Trim();
        EvaluatedAtUtc = evaluatedAtUtc == default ? DateTimeOffset.UtcNow : evaluatedAtUtc;
    }

    public Guid HypothesisId { get; private set; }

    public OpportunityPromotionGateKind GateKind { get; private set; }

    public OpportunityPromotionGateStatus Status { get; private set; }

    public string Evaluator { get; private set; }

    public string Reason { get; private set; }

    public string MetricsJson { get; private set; }

    public string EvidenceIdsJson { get; private set; }

    public DateTimeOffset EvaluatedAtUtc { get; private set; }

    public bool IsPassed => Status == OpportunityPromotionGateStatus.Passed;

    private static string Required(string value, string paramName, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{paramName} cannot be empty.", paramName);
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}
