using System.Text.Json;
using Autotrade.Api.Controllers;
using Autotrade.ArcSettlement.Application.Contract.Provenance;
using Microsoft.AspNetCore.Mvc;

namespace Autotrade.Api.Tests;

public sealed class ArcProvenanceControllerContractTests
{
    [Fact]
    public async Task GetAsync_ReturnsNotFoundForMissingProvenance()
    {
        var controller = new ArcProvenanceController(new FakeArcStrategyProvenanceService());

        var result = await controller.GetAsync(ProvenanceHash, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task GetAsync_ReturnsSubscriberSafeExplanationWithoutSecrets()
    {
        var service = new FakeArcStrategyProvenanceService
        {
            Explanation = CreateExplanation()
        };
        var controller = new ArcProvenanceController(service);

        var result = await controller.GetAsync(ProvenanceHash, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var explanation = Assert.IsType<ArcSubscriberProvenanceExplanation>(ok.Value);
        Assert.Equal(ProvenanceHash, service.LastRequestedHash);
        Assert.Equal(ArcProvenanceSourceModule.OpportunityDiscovery, explanation.SourceModule);
        Assert.Equal("Subscriber-safe source summary.", explanation.Evidence[0].Summary);

        var json = JsonSerializer.Serialize(ok.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.DoesNotContain("privateKey", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("apiSecret", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("signature", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ARC_SETTLEMENT_PRIVATE_KEY", json, StringComparison.Ordinal);
    }

    private const string ProvenanceHash = "0xaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    private static ArcSubscriberProvenanceExplanation CreateExplanation()
        => new(
            ProvenanceHash,
            ArcProvenanceSourceModule.OpportunityDiscovery,
            "opportunity-phase8-1",
            "0x9999999999999999999999999999999999999999",
            "demo-polymarket-market",
            "dual_leg_arbitrage",
            ArcProvenanceValidationStatus.Published,
            [
                new ArcProvenanceEvidenceReference(
                    "ev-1",
                    "Polymarket order book",
                    "Subscriber-safe source summary.",
                    "0xbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
                    SourceUri: "artifact://ev-1")
            ],
            "0xcccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc",
            "0xdddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd",
            "0xeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee",
            GeneratedPackageHash: null,
            "0xffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff",
            EvidenceUri: "artifacts/arc-hackathon/demo-run/provenance/opportunity-phase8-1.json",
            "Subscriber-safe summary only; raw prompts and local files may remain private or redacted.",
            DateTimeOffset.Parse("2026-05-12T11:00:00Z"));

    private sealed class FakeArcStrategyProvenanceService : IArcStrategyProvenanceService
    {
        public string? LastRequestedHash { get; private set; }

        public ArcSubscriberProvenanceExplanation? Explanation { get; init; }

        public Task<ArcStrategyProvenanceRecord> ExportOpportunityAsync(
            BuildOpportunityProvenanceRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<ArcStrategyProvenanceRecord> AnchorGeneratedPackageAsync(
            BuildGeneratedPackageProvenanceRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<ArcStrategyProvenanceRecord?> GetAsync(
            string provenanceHash,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<ArcSubscriberProvenanceExplanation?> GetSubscriberExplanationAsync(
            string provenanceHash,
            CancellationToken cancellationToken = default)
        {
            LastRequestedHash = provenanceHash;
            return Task.FromResult(Explanation);
        }
    }
}
