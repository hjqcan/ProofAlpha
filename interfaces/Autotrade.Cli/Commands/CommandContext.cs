// ============================================================================
// 命令处理器接口
// ============================================================================
// 定义 CLI 命令处理器的统一接口

using Microsoft.Extensions.Hosting;

namespace Autotrade.Cli.Commands;

/// <summary>
/// 标准退出码。
/// </summary>
public static class ExitCodes
{
    /// <summary>成功。</summary>
    public const int Success = 0;
    /// <summary>一般错误。</summary>
    public const int GeneralError = 1;
    /// <summary>未找到。</summary>
    public const int NotFound = 2;
    /// <summary>验证失败。</summary>
    public const int ValidationFailed = 3;
    /// <summary>用户取消。</summary>
    public const int UserCancelled = 4;
}

/// <summary>
/// CLI 命令处理结果。
/// </summary>
public readonly struct CommandResult
{
    /// <summary>退出码。</summary>
    public int ExitCode { get; init; }
    /// <summary>消息。</summary>
    public string? Message { get; init; }
    /// <summary>结构化数据。</summary>
    public object? Data { get; init; }
    /// <summary>错误码。</summary>
    public string? ErrorCode { get; init; }

    public static CommandResult Success(string? message = null, object? data = null) =>
        new() { ExitCode = ExitCodes.Success, Message = message, Data = data };

    public static CommandResult Fail(string message, int exitCode = ExitCodes.GeneralError, string? errorCode = null) =>
        new() { ExitCode = exitCode, Message = message, ErrorCode = errorCode };

    public static CommandResult NotFound(string item) =>
        new() { ExitCode = ExitCodes.NotFound, Message = $"未找到: {item}", ErrorCode = "NOT_FOUND" };

    public static CommandResult ValidationError(string message) =>
        new() { ExitCode = ExitCodes.ValidationFailed, Message = message, ErrorCode = "VALIDATION_FAILED" };

    public static CommandResult Cancelled(string? message = null) =>
        new() { ExitCode = ExitCodes.UserCancelled, Message = message ?? "操作已取消", ErrorCode = "CANCELLED" };
}

/// <summary>
/// 全局选项。
/// </summary>
public sealed class GlobalOptions
{
    /// <summary>是否以 JSON 格式输出。</summary>
    public bool JsonOutput { get; init; }
    /// <summary>非交互模式（禁止所有确认提示）。</summary>
    public bool NonInteractive { get; init; }
    /// <summary>自动确认破坏性操作。</summary>
    public bool AutoConfirm { get; init; }
    /// <summary>禁用彩色输出。</summary>
    public bool NoColor { get; init; }
}

/// <summary>
/// 命令上下文，包含命令执行所需的依赖。
/// </summary>
public sealed class CommandContext
{
    public required IHost Host { get; init; }
    public required IServiceProvider Services { get; init; }
    public bool JsonOutput { get; init; }
    public GlobalOptions GlobalOptions { get; init; } = new();
}
