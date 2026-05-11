using Autotrade.ArcSettlement.Application.Contract.Proofs;

namespace Autotrade.ArcSettlement.Application.Proofs;

public interface IArcProofExportService
{
    ArcProofExportResult Export(
        string exportDirectory,
        ArcStrategySignalProofDocument signalProof,
        ArcStrategyOutcomeProofDocument outcomeProof,
        ArcUtilityMetricsDocument utilityMetrics,
        DateTimeOffset exportedAtUtc);
}

public sealed class ArcProofExportService(
    IArcProofHashService hashService,
    IArcProofRedactionGuard redactionGuard) : IArcProofExportService
{
    public ArcProofExportResult Export(
        string exportDirectory,
        ArcStrategySignalProofDocument signalProof,
        ArcStrategyOutcomeProofDocument outcomeProof,
        ArcUtilityMetricsDocument utilityMetrics,
        DateTimeOffset exportedAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(exportDirectory);

        redactionGuard.ValidatePublicProof(signalProof);
        redactionGuard.ValidatePublicProof(outcomeProof);
        redactionGuard.ValidatePublicProof(utilityMetrics);

        Directory.CreateDirectory(exportDirectory);

        var manifest = new ArcProofHashManifest(
            "arc-proof-hash-manifest.v1",
            hashService.HashSignal(signalProof),
            hashService.HashOutcome(outcomeProof),
            hashService.HashUtilityMetrics(utilityMetrics),
            Array.Empty<string>(),
            exportedAtUtc);

        redactionGuard.ValidatePublicProof(manifest);

        var signalPath = Path.Combine(exportDirectory, "signal-proof.json");
        var outcomePath = Path.Combine(exportDirectory, "outcome-proof.json");
        var metricsPath = Path.Combine(exportDirectory, "utility-metrics.json");
        var manifestPath = Path.Combine(exportDirectory, "hash-manifest.json");

        WriteJson(signalPath, signalProof);
        WriteJson(outcomePath, outcomeProof);
        WriteJson(metricsPath, utilityMetrics);
        WriteJson(manifestPath, manifest);

        return new ArcProofExportResult(exportDirectory, signalPath, outcomePath, metricsPath, manifestPath);
    }

    private static void WriteJson<T>(string path, T value)
        => File.WriteAllText(path, ArcProofJson.SerializeStable(value));
}

