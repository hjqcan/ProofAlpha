using NetDevPack.Domain;

namespace Autotrade.Strategy.Domain.Entities;

public sealed class CommandAuditLog : Entity, IAggregateRoot
{
    // EF Core
    private CommandAuditLog()
    {
        CommandName = string.Empty;
        ArgumentsJson = string.Empty;
        CreatedAtUtc = DateTimeOffset.UtcNow;
    }

    public CommandAuditLog(
        string commandName,
        string argumentsJson,
        string? actor,
        bool success,
        int exitCode,
        long durationMs,
        DateTimeOffset createdAtUtc)
    {
        CommandName = string.IsNullOrWhiteSpace(commandName)
            ? throw new ArgumentException("CommandName cannot be empty.", nameof(commandName))
            : commandName.Trim();

        ArgumentsJson = string.IsNullOrWhiteSpace(argumentsJson)
            ? "{}"
            : argumentsJson.Trim();

        Actor = string.IsNullOrWhiteSpace(actor) ? null : actor.Trim();
        Success = success;
        ExitCode = exitCode;
        DurationMs = durationMs < 0 ? 0 : durationMs;
        CreatedAtUtc = createdAtUtc == default ? DateTimeOffset.UtcNow : createdAtUtc;
    }

    public string CommandName { get; private set; }

    public string ArgumentsJson { get; private set; }

    public string? Actor { get; private set; }

    public bool Success { get; private set; }

    public int ExitCode { get; private set; }

    public long DurationMs { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }
}
