namespace Autotrade.Trading.Application.Contract.Execution;

public sealed class LiveArmingOptions
{
    public const string SectionName = "LiveArming";

    public string EvidenceFilePath { get; set; } = string.Empty;

    public int ExpirationMinutes { get; set; } = 240;

    public int MaxAccountSyncAgeSeconds { get; set; } = 300;

    public string ConfigVersion { get; set; } = "local";

    public string RequiredArmConfirmationText { get; set; } = "ARM LIVE";

    public string RequiredDisarmConfirmationText { get; set; } = "DISARM LIVE";
}

public sealed record LiveArmingRequest(
    string Actor,
    string? Reason,
    string? ConfirmationText);

public sealed record LiveDisarmingRequest(
    string Actor,
    string? Reason,
    string? ConfirmationText);

public sealed record LiveArmingRiskSummary(
    decimal TotalCapital,
    decimal AvailableCapital,
    decimal CapitalUtilizationPct,
    decimal OpenNotional,
    int OpenOrders,
    int UnhedgedExposures,
    bool KillSwitchActive);

public sealed record LiveArmingEvidence(
    string EvidenceId,
    string Operator,
    string? Reason,
    DateTimeOffset ArmedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    string ConfigVersion,
    string ConfigFingerprint,
    LiveArmingRiskSummary RiskSummary,
    IReadOnlyList<string> PassedReadinessCheckIds);

public sealed record LiveArmingStatus(
    bool IsArmed,
    string State,
    string Reason,
    string ConfigVersion,
    DateTimeOffset CheckedAtUtc,
    LiveArmingEvidence? Evidence,
    IReadOnlyList<string> BlockingReasons);

public sealed record LiveArmingResult(
    bool Accepted,
    string Status,
    string Message,
    LiveArmingStatus CurrentStatus);

public interface ILiveArmingService
{
    Task<LiveArmingStatus> GetStatusAsync(CancellationToken cancellationToken = default);

    Task<LiveArmingResult> ArmAsync(
        LiveArmingRequest request,
        CancellationToken cancellationToken = default);

    Task<LiveArmingResult> DisarmAsync(
        LiveDisarmingRequest request,
        CancellationToken cancellationToken = default);

    Task<LiveArmingStatus> RequireArmedAsync(CancellationToken cancellationToken = default);
}
