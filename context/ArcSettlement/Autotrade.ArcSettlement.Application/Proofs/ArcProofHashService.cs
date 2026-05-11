using System.Security.Cryptography;
using System.Text;
using Autotrade.ArcSettlement.Application.Contract.Proofs;

namespace Autotrade.ArcSettlement.Application.Proofs;

public interface IArcProofHashService
{
    string HashSignal(ArcStrategySignalProofDocument document);

    string HashOutcome(ArcStrategyOutcomeProofDocument document);

    string HashRiskEnvelope(ArcRiskEnvelopeDocument document);

    IReadOnlyList<string> HashEvidenceSummaries(IReadOnlyList<ArcEvidenceSummaryDocument> documents);

    string HashUtilityMetrics(ArcUtilityMetricsDocument document);
}

public sealed class ArcProofHashService : IArcProofHashService
{
    public string HashSignal(ArcStrategySignalProofDocument document)
        => HashPayload(Normalize(document));

    public string HashOutcome(ArcStrategyOutcomeProofDocument document)
        => HashPayload(Normalize(document));

    public string HashRiskEnvelope(ArcRiskEnvelopeDocument document)
        => HashPayload(Normalize(document));

    public IReadOnlyList<string> HashEvidenceSummaries(IReadOnlyList<ArcEvidenceSummaryDocument> documents)
        => documents
            .OrderBy(document => document.EvidenceId, StringComparer.Ordinal)
            .ThenBy(document => document.ContentHash, StringComparer.Ordinal)
            .Select(HashPayload)
            .ToArray();

    public string HashUtilityMetrics(ArcUtilityMetricsDocument document)
        => HashPayload(document);

    private static string HashPayload<T>(T payload)
    {
        var json = ArcProofJson.SerializeStable(payload);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static ArcStrategySignalProofDocument Normalize(ArcStrategySignalProofDocument document)
        => document with
        {
            EvidenceIds = SortStrings(document.EvidenceIds)
        };

    private static ArcStrategyOutcomeProofDocument Normalize(ArcStrategyOutcomeProofDocument document)
        => document with
        {
            ClientOrderIds = SortStrings(document.ClientOrderIds),
            OrderEventIds = SortStrings(document.OrderEventIds),
            TradeIds = SortStrings(document.TradeIds)
        };

    private static ArcRiskEnvelopeDocument Normalize(ArcRiskEnvelopeDocument document)
        => document with
        {
            ConstraintIds = SortStrings(document.ConstraintIds)
        };

    private static IReadOnlyList<string> SortStrings(IReadOnlyList<string> values)
        => values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
}

