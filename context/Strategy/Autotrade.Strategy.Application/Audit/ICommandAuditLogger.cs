// ============================================================================
// 命令审计日志记录器接口
// ============================================================================

namespace Autotrade.Strategy.Application.Audit;

/// <summary>
/// 命令审计日志记录器接口。
/// </summary>
public interface ICommandAuditLogger
{
    /// <summary>
    /// 记录命令审计条目。
    /// </summary>
    Task LogAsync(CommandAuditEntry entry, CancellationToken cancellationToken = default);
}
