// ============================================================================
// 命令审计条目
// ============================================================================
// 记录 CLI 命令执行的审计信息。
// ============================================================================

namespace Autotrade.Strategy.Application.Audit;

/// <summary>
/// 命令审计条目。
/// 记录 CLI 命令的执行信息用于审计追踪。
/// </summary>
/// <param name="CommandName">命令名称。</param>
/// <param name="ArgumentsJson">命令参数（JSON 格式）。</param>
/// <param name="Actor">执行者（用户名）。</param>
/// <param name="Success">是否成功。</param>
/// <param name="ExitCode">退出码。</param>
/// <param name="DurationMs">执行耗时（毫秒）。</param>
/// <param name="TimestampUtc">执行时间戳。</param>
public sealed record CommandAuditEntry(
    string CommandName,
    string ArgumentsJson,
    string? Actor,
    bool Success,
    int ExitCode,
    long DurationMs,
    DateTimeOffset TimestampUtc);
