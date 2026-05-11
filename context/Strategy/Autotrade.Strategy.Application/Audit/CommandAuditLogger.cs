// ============================================================================
// 命令审计日志记录器
// ============================================================================
// 将 CLI 命令执行信息持久化到数据库。
// ============================================================================

using Autotrade.Strategy.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace Autotrade.Strategy.Application.Audit;

/// <summary>
/// 命令审计日志记录器。
/// 将命令执行信息持久化到审计日志表。
/// </summary>
public sealed class CommandAuditLogger : ICommandAuditLogger
{
    private readonly ICommandAuditRepository _repository;
    private readonly ILogger<CommandAuditLogger> _logger;

    public CommandAuditLogger(ICommandAuditRepository repository, ILogger<CommandAuditLogger> logger)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 记录命令审计条目。
    /// </summary>
    public async Task LogAsync(CommandAuditEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        try
        {
            var log = new CommandAuditLog(
                entry.CommandName,
                entry.ArgumentsJson,
                entry.Actor,
                entry.Success,
                entry.ExitCode,
                entry.DurationMs,
                entry.TimestampUtc);

            await _repository.AddAsync(log, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug("Command audit log write timed out for {CommandName}", entry.CommandName);
        }
        catch (Exception ex)
        {
            // 审计日志失败不应影响主流程
            _logger.LogWarning(ex, "Failed to persist command audit log: {CommandName}", entry.CommandName);
        }
    }
}
