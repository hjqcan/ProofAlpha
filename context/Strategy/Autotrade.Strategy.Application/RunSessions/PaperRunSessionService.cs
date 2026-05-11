using System.Text.Json;
using Autotrade.Application.RunSessions;
using Autotrade.Strategy.Domain.Entities;
using Autotrade.Trading.Application.Contract.Execution;
using Microsoft.Extensions.Options;

namespace Autotrade.Strategy.Application.RunSessions;

public sealed class PaperRunSessionService(
    IPaperRunSessionRepository repository,
    IOptions<ExecutionOptions> executionOptions) : IPaperRunSessionService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IPaperRunSessionRepository _repository =
        repository ?? throw new ArgumentNullException(nameof(repository));
    private readonly ExecutionOptions _executionOptions =
        executionOptions?.Value ?? throw new ArgumentNullException(nameof(executionOptions));

    public async Task<PaperRunSessionRecord> StartOrRecoverAsync(
        PaperRunSessionStartRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsurePaperMode();

        var executionMode = _executionOptions.Mode.ToString();
        var active = await _repository.GetActiveAsync(executionMode, cancellationToken).ConfigureAwait(false);
        if (active is not null && !request.ForceNewSession)
        {
            return ToRecord(active, recovered: true);
        }

        if (active is not null)
        {
            active.Stop(request.StartedAtUtc ?? DateTimeOffset.UtcNow, "Superseded by explicit Paper run start.");
            await _repository.UpdateAsync(active, cancellationToken).ConfigureAwait(false);
        }

        var session = new PaperRunSession(
            executionMode,
            NormalizeText(request.ConfigVersion, "ConfigVersion"),
            SerializeStrategies(request.Strategies),
            NormalizeRiskProfile(request.RiskProfileJson),
            NormalizeText(request.OperatorSource, "OperatorSource"),
            request.StartedAtUtc ?? DateTimeOffset.UtcNow);

        try
        {
            await _repository.AddAsync(session, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch when (!request.ForceNewSession)
        {
            var recovered = await _repository.GetActiveAsync(executionMode, cancellationToken).ConfigureAwait(false);
            if (recovered is not null)
            {
                return ToRecord(recovered, recovered: true);
            }

            throw;
        }

        return ToRecord(session, recovered: false);
    }

    public async Task<PaperRunSessionRecord?> StopAsync(
        PaperRunSessionStopRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.SessionId == Guid.Empty)
        {
            return null;
        }

        var session = await _repository.GetAsync(request.SessionId, cancellationToken).ConfigureAwait(false);
        if (session is null)
        {
            return null;
        }

        session.Stop(
            request.StoppedAtUtc ?? DateTimeOffset.UtcNow,
            string.IsNullOrWhiteSpace(request.Reason)
                ? $"Stopped by {NormalizeOptional(request.OperatorSource) ?? "unknown"}"
                : request.Reason);

        await _repository.UpdateAsync(session, cancellationToken).ConfigureAwait(false);
        return ToRecord(session, recovered: false);
    }

    public async Task<PaperRunSessionRecord?> GetActiveAsync(CancellationToken cancellationToken = default)
    {
        EnsurePaperMode();
        var active = await _repository
            .GetActiveAsync(_executionOptions.Mode.ToString(), cancellationToken)
            .ConfigureAwait(false);

        return active is null ? null : ToRecord(active, recovered: true);
    }

    public async Task<PaperRunSessionRecord?> ExportAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        if (sessionId == Guid.Empty)
        {
            return null;
        }

        var session = await _repository.GetAsync(sessionId, cancellationToken).ConfigureAwait(false);
        return session is null ? null : ToRecord(session, recovered: false);
    }

    public async Task<RunSessionIdentity?> GetCurrentAsync(
        string executionMode,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(executionMode, ExecutionMode.Paper.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var active = await _repository
            .GetActiveAsync(ExecutionMode.Paper.ToString(), cancellationToken)
            .ConfigureAwait(false);

        return active is null
            ? null
            : new RunSessionIdentity(active.Id, active.ExecutionMode, active.ConfigVersion, active.StartedAtUtc);
    }

    private void EnsurePaperMode()
    {
        if (_executionOptions.Mode != ExecutionMode.Paper)
        {
            throw new InvalidOperationException("Paper run sessions require Execution:Mode Paper.");
        }
    }

    private static PaperRunSessionRecord ToRecord(PaperRunSession session, bool recovered)
        => new(
            session.Id,
            session.ExecutionMode,
            session.ConfigVersion,
            DeserializeStrategies(session.StrategiesJson),
            session.RiskProfileJson,
            session.OperatorSource,
            session.StartedAtUtc,
            session.StoppedAtUtc,
            session.StopReason,
            session.IsActive,
            recovered);

    private static string SerializeStrategies(IReadOnlyCollection<string> strategies)
    {
        if (strategies.Count == 0)
        {
            throw new ArgumentException("At least one strategy is required.", nameof(strategies));
        }

        var normalized = strategies
            .Where(strategy => !string.IsNullOrWhiteSpace(strategy))
            .Select(strategy => strategy.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(strategy => strategy, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (normalized.Length == 0)
        {
            throw new ArgumentException("At least one strategy is required.", nameof(strategies));
        }

        return JsonSerializer.Serialize(normalized, JsonOptions);
    }

    private static IReadOnlyList<string> DeserializeStrategies(string strategiesJson)
        => JsonSerializer.Deserialize<IReadOnlyList<string>>(strategiesJson, JsonOptions) ?? [];

    private static string NormalizeRiskProfile(string riskProfileJson)
    {
        if (string.IsNullOrWhiteSpace(riskProfileJson))
        {
            return "{}";
        }

        using var document = JsonDocument.Parse(riskProfileJson);
        return document.RootElement.GetRawText();
    }

    private static string NormalizeText(string value, string name)
        => string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException($"{name} cannot be empty.", name)
            : value.Trim();

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
