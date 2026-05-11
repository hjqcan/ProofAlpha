// ============================================================================
// 命令审计日志仓储接口
// ============================================================================

using Autotrade.Strategy.Domain.Entities;

namespace Autotrade.Strategy.Application.Audit;

/// <summary>
/// 命令审计日志仓储接口。
/// </summary>
public interface ICommandAuditRepository
{
    /// <summary>
    /// 添加审计日志记录。
    /// </summary>
    Task AddAsync(CommandAuditLog log, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CommandAuditLog>> QueryAsync(
        CommandAuditQuery query,
        CancellationToken cancellationToken = default);
}
