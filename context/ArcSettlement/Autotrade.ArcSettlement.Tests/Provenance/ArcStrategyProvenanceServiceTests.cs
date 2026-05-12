using Autotrade.ArcSettlement.Application.Contract.Proofs;
using Autotrade.ArcSettlement.Application.Contract.Provenance;
using Autotrade.ArcSettlement.Application.Proofs;
using Autotrade.ArcSettlement.Application.Provenance;

namespace Autotrade.ArcSettlement.Tests.Provenance;

public sealed class ArcStrategyProvenanceServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 5, 12, 11, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ExportOpportunityAsync_WhenOpportunityApproved_PersistsDeterministicProvenance()
    {
        var service = CreateService();
        var first = await service.ExportOpportunityAsync(CreateOpportunityRequest(
            evidence:
            [
                CreateEvidence("ev-2"),
                CreateEvidence("ev-1")
            ]));

        var second = await service.ExportOpportunityAsync(CreateOpportunityRequest(
            evidence:
            [
                CreateEvidence("ev-1"),
                CreateEvidence("ev-2")
            ]));

        Assert.Equal(first.ProvenanceHash, second.ProvenanceHash);
        Assert.Equal(ArcProvenanceSourceModule.OpportunityDiscovery, first.Document.SourceModule);
        Assert.Equal(ArcProvenanceValidationStatus.Approved, first.Document.ValidationStatus);
        Assert.Equal(["ev-1", "ev-2"], first.Document.EvidenceIds);
        Assert.StartsWith("0x", first.Document.EvidenceSummaryHash, StringComparison.Ordinal);
        Assert.StartsWith("0x", first.Document.LlmOutputHash, StringComparison.Ordinal);
        Assert.Null(first.Document.GeneratedPackageHash);
    }

    [Theory]
    [InlineData(ArcOpportunityProvenanceStatus.Candidate)]
    [InlineData(ArcOpportunityProvenanceStatus.NeedsReview)]
    [InlineData(ArcOpportunityProvenanceStatus.Rejected)]
    [InlineData(ArcOpportunityProvenanceStatus.Expired)]
    public async Task ExportOpportunityAsync_WhenOpportunityIsNotTradeReady_Throws(
        ArcOpportunityProvenanceStatus status)
    {
        var service = CreateService();

        var ex = await Assert.ThrowsAsync<ArcProvenanceRejectedException>(
            () => service.ExportOpportunityAsync(CreateOpportunityRequest(status: status)));

        Assert.Equal("OPPORTUNITY_NOT_TRADE_READY", ex.ErrorCode);
    }

    [Fact]
    public async Task AnchorGeneratedPackageAsync_WhenPackagePassesGate_PersistsPackageHash()
    {
        var service = CreateService();

        var record = await service.AnchorGeneratedPackageAsync(CreateGeneratedPackageRequest());

        Assert.Equal(ArcProvenanceSourceModule.SelfImprove, record.Document.SourceModule);
        Assert.Equal(ArcProvenanceValidationStatus.StaticValidated, record.Document.ValidationStatus);
        Assert.Equal("0xaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", record.Document.GeneratedPackageHash);
        Assert.StartsWith("0x", record.ProvenanceHash, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ArcGeneratedPackageValidationStage.Generated, "{\"passed\":true,\"errors\":[]}")]
    [InlineData(ArcGeneratedPackageValidationStage.StaticValidated, "{\"passed\":false,\"errors\":[\"missing replay\"]}")]
    public async Task AnchorGeneratedPackageAsync_WhenPackageIsBelowGate_Throws(
        ArcGeneratedPackageValidationStage stage,
        string validationSummaryJson)
    {
        var service = CreateService();

        var ex = await Assert.ThrowsAsync<ArcProvenanceRejectedException>(
            () => service.AnchorGeneratedPackageAsync(CreateGeneratedPackageRequest(
                validationStage: stage,
                validationSummaryJson: validationSummaryJson)));

        Assert.Equal("GENERATED_PACKAGE_VALIDATION_FAILED", ex.ErrorCode);
    }

    [Fact]
    public async Task ExportOpportunityAsync_WhenEvidenceContainsSecretLikeMaterial_Throws()
    {
        var service = CreateService();

        await Assert.ThrowsAsync<ArcProofRedactionException>(
            () => service.ExportOpportunityAsync(CreateOpportunityRequest(
                evidence:
                [
                    CreateEvidence("ev-secret") with { Summary = "apiSecret=not-for-public-output" }
                ])));
    }

    [Fact]
    public async Task GetSubscriberExplanationAsync_ReturnsSubscriberSafeReadModel()
    {
        var service = CreateService();
        var record = await service.ExportOpportunityAsync(CreateOpportunityRequest(
            status: ArcOpportunityProvenanceStatus.Published));

        var explanation = await service.GetSubscriberExplanationAsync(record.ProvenanceHash);

        Assert.NotNull(explanation);
        Assert.Equal(record.ProvenanceHash, explanation.ProvenanceHash);
        Assert.Equal(ArcProvenanceValidationStatus.Published, explanation.ValidationStatus);
        Assert.Single(explanation.Evidence);
        Assert.Contains("Subscriber-safe", explanation.PrivacyNote, StringComparison.Ordinal);
    }

    private static ArcStrategyProvenanceService CreateService()
        => new(
            new InMemoryProvenanceStore(),
            new ArcProofRedactionGuard(),
            new FixedTimeProvider(Now));

    private static BuildOpportunityProvenanceRequest CreateOpportunityRequest(
        ArcOpportunityProvenanceStatus status = ArcOpportunityProvenanceStatus.Approved,
        IReadOnlyList<ArcProvenanceEvidenceReference>? evidence = null)
        => new(
            SourceId: "opportunity-phase8-1",
            AgentId: "0x9999999999999999999999999999999999999999",
            MarketId: "demo-polymarket-market",
            StrategyId: "dual_leg_arbitrage",
            Status: status,
            Evidence: evidence ?? [CreateEvidence("ev-1")],
            LlmOutputJson: "{\"thesis\":\"repricing lag\",\"confidence\":0.82}",
            CompiledPolicyJson: "{\"entryMaxPrice\":0.49,\"takeProfitPrice\":0.57}",
            RiskEnvelopeJson: "{\"riskTier\":\"paper\",\"maxNotionalUsdc\":100}",
            CreatedAtUtc: Now,
            EvidenceUri: "artifacts/arc-hackathon/demo-run/provenance/opportunity-phase8-1.json");

    private static BuildGeneratedPackageProvenanceRequest CreateGeneratedPackageRequest(
        ArcGeneratedPackageValidationStage validationStage = ArcGeneratedPackageValidationStage.StaticValidated,
        string validationSummaryJson = "{\"passed\":true,\"errors\":[]}")
        => new(
            SourceId: "generated-strategy-version-1",
            AgentId: "0x9999999999999999999999999999999999999999",
            MarketId: "demo-polymarket-market",
            StrategyId: "generated_repricing_lag_v1",
            PackageHash: "0xaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            ManifestJson: "{\"strategyId\":\"generated_repricing_lag_v1\",\"version\":\"v1\"}",
            RiskEnvelopeJson: "{\"riskTier\":\"paper\",\"maxNotionalUsdc\":50}",
            ValidationSummaryJson: validationSummaryJson,
            ValidationStage: validationStage,
            Evidence: [CreateEvidence("episode-1")],
            LlmOutputJson: "{\"kind\":\"generatedStrategy\",\"strategyId\":\"generated_repricing_lag_v1\"}",
            CreatedAtUtc: Now,
            EvidenceUri: "artifacts/arc-hackathon/demo-run/provenance/generated-strategy-version-1.json");

    private static ArcProvenanceEvidenceReference CreateEvidence(string id)
        => new(
            id,
            $"Evidence {id}",
            $"Subscriber-safe summary for {id}.",
            $"0x{Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(id)))
                .ToLowerInvariant()}",
            SourceUri: $"artifact://{id}",
            ObservedAtUtc: Now.AddMinutes(-5));

    private sealed class InMemoryProvenanceStore : IArcStrategyProvenanceStore
    {
        private readonly List<ArcStrategyProvenanceRecord> _records = [];

        public Task<ArcStrategyProvenanceRecord?> GetByHashAsync(
            string provenanceHash,
            CancellationToken cancellationToken = default)
            => Task.FromResult(_records.FirstOrDefault(record => IsSame(record.ProvenanceHash, provenanceHash)));

        public Task<ArcStrategyProvenanceRecord?> GetBySourceAsync(
            ArcProvenanceSourceModule sourceModule,
            string sourceId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(_records.FirstOrDefault(record =>
                record.Document.SourceModule == sourceModule && IsSame(record.Document.SourceId, sourceId)));

        public Task<IReadOnlyList<ArcStrategyProvenanceRecord>> ListAsync(
            int limit,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ArcStrategyProvenanceRecord>>(
                _records.OrderByDescending(record => record.RecordedAtUtc).Take(limit).ToArray());

        public Task UpsertAsync(
            ArcStrategyProvenanceRecord record,
            CancellationToken cancellationToken = default)
        {
            var index = _records.FindIndex(item => IsSame(item.ProvenanceHash, record.ProvenanceHash));
            if (index >= 0)
            {
                _records[index] = record;
            }
            else
            {
                _records.Add(record);
            }

            return Task.CompletedTask;
        }

        private static bool IsSame(string left, string right)
            => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
