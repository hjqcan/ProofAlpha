using NetDevPack.Domain;

namespace Autotrade.Strategy.Domain.Entities;

public sealed class PaperRunSession : Entity, IAggregateRoot
{
    private PaperRunSession()
    {
        ExecutionMode = string.Empty;
        ConfigVersion = string.Empty;
        StrategiesJson = "[]";
        RiskProfileJson = "{}";
        OperatorSource = string.Empty;
        StartedAtUtc = DateTimeOffset.UtcNow;
    }

    public PaperRunSession(
        string executionMode,
        string configVersion,
        string strategiesJson,
        string riskProfileJson,
        string operatorSource,
        DateTimeOffset startedAtUtc)
    {
        ExecutionMode = RequireText(executionMode, nameof(executionMode));
        ConfigVersion = RequireText(configVersion, nameof(configVersion));
        StrategiesJson = RequireText(strategiesJson, nameof(strategiesJson));
        RiskProfileJson = RequireText(riskProfileJson, nameof(riskProfileJson));
        OperatorSource = RequireText(operatorSource, nameof(operatorSource));
        StartedAtUtc = startedAtUtc == default ? DateTimeOffset.UtcNow : startedAtUtc;
    }

    public string ExecutionMode { get; private set; }

    public string ConfigVersion { get; private set; }

    public string StrategiesJson { get; private set; }

    public string RiskProfileJson { get; private set; }

    public string OperatorSource { get; private set; }

    public DateTimeOffset StartedAtUtc { get; private set; }

    public DateTimeOffset? StoppedAtUtc { get; private set; }

    public string? StopReason { get; private set; }

    public bool IsActive => StoppedAtUtc is null;

    public void Stop(DateTimeOffset stoppedAtUtc, string? reason)
    {
        if (StoppedAtUtc is not null)
        {
            return;
        }

        StoppedAtUtc = stoppedAtUtc == default ? DateTimeOffset.UtcNow : stoppedAtUtc;
        StopReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
    }

    private static string RequireText(string value, string parameterName)
        => string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException($"{parameterName} cannot be empty.", parameterName)
            : value.Trim();
}
