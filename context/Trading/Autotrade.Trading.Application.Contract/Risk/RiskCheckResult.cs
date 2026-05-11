using Autotrade.Trading.Domain.Shared.Enums;

namespace Autotrade.Trading.Application.Contract.Risk;

/// <summary>
/// Result of a risk validation.
/// </summary>
public sealed record RiskCheckResult
{
    public bool Allowed { get; init; }

    public RiskAction Action { get; init; } = RiskAction.Allow;

    public string? Code { get; init; }

    public string? Message { get; init; }

    public RiskSeverity Severity { get; init; } = RiskSeverity.Info;

    public static RiskCheckResult Allow() => new() { Allowed = true, Action = RiskAction.Allow };

    public static RiskCheckResult Block(
        string code,
        string message,
        RiskSeverity severity = RiskSeverity.Warning,
        RiskAction action = RiskAction.Block)
        => new()
        {
            Allowed = false,
            Action = action,
            Code = code,
            Message = message,
            Severity = severity
        };
}
