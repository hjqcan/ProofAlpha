namespace Autotrade.Application.RunSessions;

public sealed record RunSessionIdentity(
    Guid SessionId,
    string ExecutionMode,
    string ConfigVersion,
    DateTimeOffset StartedAtUtc);

public interface IRunSessionAccessor
{
    Task<RunSessionIdentity?> GetCurrentAsync(
        string executionMode,
        CancellationToken cancellationToken = default);
}
