using Autotrade.Application.RunSessions;

namespace Autotrade.Strategy.Application.RunSessions;

public sealed record PaperRunSessionStartRequest(
    string ConfigVersion,
    IReadOnlyCollection<string> Strategies,
    string RiskProfileJson,
    string OperatorSource,
    bool ForceNewSession = false,
    DateTimeOffset? StartedAtUtc = null);

public sealed record PaperRunSessionStopRequest(
    Guid SessionId,
    string? OperatorSource,
    string? Reason,
    DateTimeOffset? StoppedAtUtc = null);

public sealed record PaperRunSessionRecord(
    Guid SessionId,
    string ExecutionMode,
    string ConfigVersion,
    IReadOnlyList<string> Strategies,
    string RiskProfileJson,
    string OperatorSource,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? StoppedAtUtc,
    string? StopReason,
    bool IsActive,
    bool Recovered);

public interface IPaperRunSessionService : IRunSessionAccessor
{
    Task<PaperRunSessionRecord> StartOrRecoverAsync(
        PaperRunSessionStartRequest request,
        CancellationToken cancellationToken = default);

    Task<PaperRunSessionRecord?> StopAsync(
        PaperRunSessionStopRequest request,
        CancellationToken cancellationToken = default);

    Task<PaperRunSessionRecord?> GetActiveAsync(CancellationToken cancellationToken = default);

    Task<PaperRunSessionRecord?> ExportAsync(Guid sessionId, CancellationToken cancellationToken = default);
}
