using System.Text.Json;
using System.Text.Json.Serialization;

namespace Autotrade.ArcSettlement.Application.Contract.Proofs;

public enum ArcProofSourceKind
{
    Opportunity,
    StrategyDecision,
    PaperRun,
    Replay,
    SelfImproveGeneratedStrategy,
    ManualFixture
}

public enum ArcProofExecutionMode
{
    Paper,
    Live,
    Replay,
    Fixture
}

public enum ArcSignalOutcomeStatus
{
    Executed,
    Rejected,
    Expired,
    Skipped,
    Failed,
    Revoked
}

public sealed record ArcStrategySignalProofDocument(
    string DocumentVersion,
    string AgentId,
    ArcProofSourceKind SourceKind,
    string SourceId,
    string StrategyId,
    string MarketId,
    string Venue,
    DateTimeOffset CreatedAtUtc,
    string ConfigVersion,
    IReadOnlyList<string> EvidenceIds,
    string OpportunityHash,
    string ReasoningHash,
    string RiskEnvelopeHash,
    decimal ExpectedEdgeBps,
    decimal MaxNotionalUsdc,
    DateTimeOffset ValidUntilUtc,
    string? GeneratedStrategyPackageHash = null,
    string? ProvenanceHash = null,
    string? EvidenceUri = null);

public sealed record ArcStrategyOutcomeProofDocument(
    string DocumentVersion,
    string SignalId,
    string ExecutionId,
    string RunSessionId,
    IReadOnlyList<string> ClientOrderIds,
    IReadOnlyList<string> OrderEventIds,
    IReadOnlyList<string> TradeIds,
    ArcProofExecutionMode PaperOrLive,
    ArcSignalOutcomeStatus Status,
    decimal? RealizedPnlBps,
    decimal? SlippageBps,
    decimal? FillRate,
    string? RiskRejectReason,
    DateTimeOffset CompletedAtUtc);

public sealed record ArcRiskEnvelopeDocument(
    string DocumentVersion,
    string StrategyId,
    string MarketId,
    ArcProofExecutionMode ExecutionMode,
    decimal MaxNotionalUsdc,
    string RiskTier,
    bool KillSwitchActive,
    bool LiveArmed,
    IReadOnlyList<string> ConstraintIds);

public sealed record ArcEvidenceSummaryDocument(
    string EvidenceId,
    string EvidenceKind,
    string SourceId,
    string ContentHash,
    DateTimeOffset ObservedAtUtc);

public sealed record ArcUtilityMetricsDocument(
    string DocumentVersion,
    string AgentId,
    string StrategyId,
    int PublishedSignalsCount,
    int ExecutedSignalsCount,
    int ExpiredSignalsCount,
    int RejectedSignalsCount,
    int SkippedSignalsCount,
    int FailedSignalsCount,
    int RevokedSignalsCount,
    int PendingSignalsCount,
    decimal? ExecutedPaperWinRate,
    decimal? AverageRealizedPnlBps,
    decimal? AverageSlippageBps,
    decimal? AverageSignalToExecutionSeconds,
    decimal? MaxPaperDrawdownBps,
    int TerminalOutcomeCount,
    int ReputationDenominator,
    DateTimeOffset CalculatedAtUtc);

public sealed record ArcProofHashManifest(
    string DocumentVersion,
    string SignalHash,
    string OutcomeHash,
    string UtilityMetricsHash,
    IReadOnlyList<string> EvidenceHashes,
    DateTimeOffset ExportedAtUtc);

public sealed record ArcProofExportPackage(
    ArcStrategySignalProofDocument SignalProof,
    ArcStrategyOutcomeProofDocument OutcomeProof,
    ArcUtilityMetricsDocument UtilityMetrics,
    ArcProofHashManifest HashManifest);

public sealed record ArcProofExportResult(
    string ExportDirectory,
    string SignalProofPath,
    string OutcomeProofPath,
    string UtilityMetricsPath,
    string HashManifestPath);

public static class ArcProofJson
{
    public static JsonSerializerOptions StableSerializerOptions { get; } = CreateStableSerializerOptions();

    public static string SerializeStable<T>(T value)
        => JsonSerializer.Serialize(value, StableSerializerOptions);

    private static JsonSerializerOptions CreateStableSerializerOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        return options;
    }
}
