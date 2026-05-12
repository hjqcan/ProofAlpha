using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Autotrade.ArcSettlement.Application.Contract.Proofs;
using Autotrade.ArcSettlement.Application.Contract.Provenance;
using Autotrade.ArcSettlement.Application.Proofs;

namespace Autotrade.ArcSettlement.Application.Provenance;

public interface IArcStrategyProvenanceStore
{
    Task<ArcStrategyProvenanceRecord?> GetByHashAsync(
        string provenanceHash,
        CancellationToken cancellationToken = default);

    Task<ArcStrategyProvenanceRecord?> GetBySourceAsync(
        ArcProvenanceSourceModule sourceModule,
        string sourceId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ArcStrategyProvenanceRecord>> ListAsync(
        int limit,
        CancellationToken cancellationToken = default);

    Task UpsertAsync(
        ArcStrategyProvenanceRecord record,
        CancellationToken cancellationToken = default);
}

public sealed class ArcStrategyProvenanceService(
    IArcStrategyProvenanceStore store,
    IArcProofRedactionGuard redactionGuard,
    TimeProvider timeProvider) : IArcStrategyProvenanceService
{
    private const string DocumentVersion = "proofalpha-arc-strategy-provenance.v1";
    private const string PrivacyNote =
        "Subscriber-safe summary only; raw prompts, private research notes, and local files may remain private or redacted.";

    private readonly IArcStrategyProvenanceStore _store = store ?? throw new ArgumentNullException(nameof(store));
    private readonly IArcProofRedactionGuard _redactionGuard =
        redactionGuard ?? throw new ArgumentNullException(nameof(redactionGuard));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

    public async Task<ArcStrategyProvenanceRecord> ExportOpportunityAsync(
        BuildOpportunityProvenanceRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureOpportunityIsTradeReady(request.Status);

        var createdAtUtc = request.CreatedAtUtc ?? _timeProvider.GetUtcNow();
        var evidence = NormalizeEvidence(request.Evidence);
        var document = CreateDocument(
            ArcProvenanceSourceModule.OpportunityDiscovery,
            request.SourceId,
            request.AgentId,
            request.MarketId,
            request.StrategyId,
            evidence,
            request.LlmOutputJson,
            request.CompiledPolicyJson,
            generatedPackageHash: null,
            request.RiskEnvelopeJson,
            request.Status == ArcOpportunityProvenanceStatus.Published
                ? ArcProvenanceValidationStatus.Published
                : ArcProvenanceValidationStatus.Approved,
            createdAtUtc);

        return await PersistAsync(document, evidence, request.EvidenceUri, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ArcStrategyProvenanceRecord> AnchorGeneratedPackageAsync(
        BuildGeneratedPackageProvenanceRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateGeneratedPackage(request);

        var createdAtUtc = request.CreatedAtUtc ?? _timeProvider.GetUtcNow();
        var evidence = NormalizeEvidence(request.Evidence);
        var document = CreateDocument(
            ArcProvenanceSourceModule.SelfImprove,
            request.SourceId,
            request.AgentId,
            request.MarketId,
            request.StrategyId,
            evidence,
            request.LlmOutputJson,
            request.ManifestJson,
            request.PackageHash,
            request.RiskEnvelopeJson,
            MapGeneratedValidationStatus(request.ValidationStage),
            createdAtUtc);

        return await PersistAsync(document, evidence, request.EvidenceUri, cancellationToken).ConfigureAwait(false);
    }

    public Task<ArcStrategyProvenanceRecord?> GetAsync(
        string provenanceHash,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provenanceHash);
        return _store.GetByHashAsync(NormalizeHash(provenanceHash), cancellationToken);
    }

    public async Task<ArcSubscriberProvenanceExplanation?> GetSubscriberExplanationAsync(
        string provenanceHash,
        CancellationToken cancellationToken = default)
    {
        var record = await GetAsync(provenanceHash, cancellationToken).ConfigureAwait(false);
        if (record is null)
        {
            return null;
        }

        var document = record.Document;
        var explanation = new ArcSubscriberProvenanceExplanation(
            record.ProvenanceHash,
            document.SourceModule,
            document.SourceId,
            document.AgentId,
            document.MarketId,
            document.StrategyId,
            document.ValidationStatus,
            record.Evidence,
            document.EvidenceSummaryHash,
            document.LlmOutputHash,
            document.CompiledPolicyHash,
            document.GeneratedPackageHash,
            document.RiskEnvelopeHash,
            record.EvidenceUri,
            record.PrivacyNote,
            document.CreatedAtUtc);

        _redactionGuard.ValidatePublicProof(explanation);
        return explanation;
    }

    private async Task<ArcStrategyProvenanceRecord> PersistAsync(
        ArcStrategyProvenanceDocument document,
        IReadOnlyList<ArcProvenanceEvidenceReference> evidence,
        string? evidenceUri,
        CancellationToken cancellationToken)
    {
        var provenanceHash = HashDocument(document);
        var record = new ArcStrategyProvenanceRecord(
            provenanceHash,
            document,
            evidence,
            string.IsNullOrWhiteSpace(evidenceUri) ? null : evidenceUri.Trim(),
            PrivacyNote,
            _timeProvider.GetUtcNow());

        _redactionGuard.ValidatePublicProof(record);
        await _store.UpsertAsync(record, cancellationToken).ConfigureAwait(false);
        return record;
    }

    private static ArcStrategyProvenanceDocument CreateDocument(
        ArcProvenanceSourceModule sourceModule,
        string sourceId,
        string agentId,
        string marketId,
        string strategyId,
        IReadOnlyList<ArcProvenanceEvidenceReference> evidence,
        string llmOutputJson,
        string compiledPolicyJson,
        string? generatedPackageHash,
        string riskEnvelopeJson,
        ArcProvenanceValidationStatus validationStatus,
        DateTimeOffset createdAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(marketId);
        ArgumentException.ThrowIfNullOrWhiteSpace(strategyId);

        return new ArcStrategyProvenanceDocument(
            DocumentVersion,
            sourceModule,
            sourceId.Trim(),
            agentId.Trim(),
            marketId.Trim(),
            strategyId.Trim(),
            evidence.Select(item => item.EvidenceId).ToArray(),
            HashPayload(evidence),
            HashTextPayload(llmOutputJson, nameof(llmOutputJson)),
            HashTextPayload(compiledPolicyJson, nameof(compiledPolicyJson)),
            string.IsNullOrWhiteSpace(generatedPackageHash) ? null : generatedPackageHash.Trim(),
            HashTextPayload(riskEnvelopeJson, nameof(riskEnvelopeJson)),
            validationStatus,
            createdAtUtc == default ? throw new ArgumentException("CreatedAtUtc cannot be default.", nameof(createdAtUtc)) : createdAtUtc);
    }

    private static IReadOnlyList<ArcProvenanceEvidenceReference> NormalizeEvidence(
        IReadOnlyList<ArcProvenanceEvidenceReference> evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        if (evidence.Count == 0)
        {
            throw new ArgumentException("Provenance requires at least one evidence reference.", nameof(evidence));
        }

        return evidence
            .Select(item =>
            {
                if (item is null)
                {
                    throw new ArgumentException("Evidence references cannot contain null entries.", nameof(evidence));
                }

                ArgumentException.ThrowIfNullOrWhiteSpace(item.EvidenceId);
                ArgumentException.ThrowIfNullOrWhiteSpace(item.Title);
                ArgumentException.ThrowIfNullOrWhiteSpace(item.Summary);
                ArgumentException.ThrowIfNullOrWhiteSpace(item.ContentHash);

                return new ArcProvenanceEvidenceReference(
                    item.EvidenceId.Trim(),
                    item.Title.Trim(),
                    item.Summary.Trim(),
                    item.ContentHash.Trim(),
                    string.IsNullOrWhiteSpace(item.SourceUri) ? null : item.SourceUri.Trim(),
                    item.ObservedAtUtc);
            })
            .OrderBy(item => item.EvidenceId, StringComparer.Ordinal)
            .ThenBy(item => item.ContentHash, StringComparer.Ordinal)
            .ToArray();
    }

    private static void EnsureOpportunityIsTradeReady(ArcOpportunityProvenanceStatus status)
    {
        if (status is ArcOpportunityProvenanceStatus.Approved or ArcOpportunityProvenanceStatus.Published)
        {
            return;
        }

        throw new ArcProvenanceRejectedException(
            "OPPORTUNITY_NOT_TRADE_READY",
            $"Opportunity status '{status}' cannot be exported as paid-signal provenance.");
    }

    private static void ValidateGeneratedPackage(BuildGeneratedPackageProvenanceRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.PackageHash);

        if (IsMissingPayload(request.ManifestJson))
        {
            throw new ArcProvenanceRejectedException(
                "GENERATED_PACKAGE_MANIFEST_MISSING",
                "Generated strategy provenance requires a manifest.");
        }

        if (IsMissingPayload(request.RiskEnvelopeJson))
        {
            throw new ArcProvenanceRejectedException(
                "GENERATED_PACKAGE_RISK_ENVELOPE_MISSING",
                "Generated strategy provenance requires a risk envelope.");
        }

        if (!ValidationStagePassesMinimumGate(request.ValidationStage)
            || !ValidationSummaryPassed(request.ValidationSummaryJson))
        {
            throw new ArcProvenanceRejectedException(
                "GENERATED_PACKAGE_VALIDATION_FAILED",
                "Generated strategy provenance requires a passed validation summary at StaticValidated or higher.");
        }
    }

    private static bool ValidationStagePassesMinimumGate(ArcGeneratedPackageValidationStage stage)
        => stage is ArcGeneratedPackageValidationStage.StaticValidated
            or ArcGeneratedPackageValidationStage.UnitTested
            or ArcGeneratedPackageValidationStage.ReplayValidated
            or ArcGeneratedPackageValidationStage.ShadowRunning
            or ArcGeneratedPackageValidationStage.PaperRunning
            or ArcGeneratedPackageValidationStage.LiveCanary
            or ArcGeneratedPackageValidationStage.Promoted;

    private static ArcProvenanceValidationStatus MapGeneratedValidationStatus(
        ArcGeneratedPackageValidationStage stage)
        => stage switch
        {
            ArcGeneratedPackageValidationStage.StaticValidated => ArcProvenanceValidationStatus.StaticValidated,
            ArcGeneratedPackageValidationStage.UnitTested => ArcProvenanceValidationStatus.UnitTested,
            ArcGeneratedPackageValidationStage.ReplayValidated => ArcProvenanceValidationStatus.ReplayValidated,
            ArcGeneratedPackageValidationStage.ShadowRunning => ArcProvenanceValidationStatus.ShadowRunning,
            ArcGeneratedPackageValidationStage.PaperRunning => ArcProvenanceValidationStatus.PaperRunning,
            ArcGeneratedPackageValidationStage.LiveCanary => ArcProvenanceValidationStatus.LiveCanary,
            ArcGeneratedPackageValidationStage.Promoted => ArcProvenanceValidationStatus.Promoted,
            _ => throw new ArcProvenanceRejectedException(
                "GENERATED_PACKAGE_VALIDATION_FAILED",
                $"Generated strategy stage '{stage}' is below the provenance anchor gate.")
        };

    private static bool ValidationSummaryPassed(string validationSummaryJson)
    {
        if (IsMissingPayload(validationSummaryJson))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(validationSummaryJson);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("passed", out var passed))
            {
                return passed.ValueKind == JsonValueKind.True;
            }

            if (root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("errors", out var errors)
                && errors.ValueKind == JsonValueKind.Array)
            {
                return !errors.EnumerateArray().Any();
            }
        }
        catch (JsonException)
        {
            return false;
        }

        return false;
    }

    private static bool IsMissingPayload(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return true;
        }

        var trimmed = payload.Trim();
        return string.Equals(trimmed, "{}", StringComparison.Ordinal)
            || string.Equals(trimmed, "[]", StringComparison.Ordinal);
    }

    private static string HashTextPayload(string payload, string paramName)
    {
        if (IsMissingPayload(payload))
        {
            throw new ArgumentException($"{paramName} cannot be empty.", paramName);
        }

        return HashString(CanonicalizePayload(payload));
    }

    private static string HashDocument(ArcStrategyProvenanceDocument document)
        => HashPayload(document with
        {
            EvidenceIds = document.EvidenceIds
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray()
        });

    private static string HashPayload<T>(T payload)
        => HashString(ArcProofJson.SerializeStable(payload));

    private static string HashString(string value)
        => $"0x{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant()}";

    private static string NormalizeHash(string value)
    {
        var trimmed = value.Trim();
        return trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? $"0x{trimmed[2..].ToLowerInvariant()}"
            : $"0x{trimmed.ToLowerInvariant()}";
    }

    private static string CanonicalizePayload(string value)
    {
        var trimmed = value.Trim();
        try
        {
            using var document = JsonDocument.Parse(trimmed);
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                WriteCanonical(document.RootElement, writer);
            }

            return Encoding.UTF8.GetString(stream.ToArray());
        }
        catch (JsonException)
        {
            return trimmed;
        }
    }

    private static void WriteCanonical(JsonElement element, Utf8JsonWriter writer)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(item => item.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(property.Value, writer);
                }

                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteCanonical(item, writer);
                }

                writer.WriteEndArray();
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }
}
