// ============================================================================
// 确认服务
// ============================================================================
// 提供破坏性操作的确认机制。
// ============================================================================

using Autotrade.Cli.Commands;

namespace Autotrade.Cli.Infrastructure;

/// <summary>
/// 确认服务。
/// 用于破坏性操作前的用户确认。
/// </summary>
public static class ConfirmationService
{
    /// <summary>
    /// 请求用户确认。
    /// </summary>
    /// <param name="message">确认提示消息。</param>
    /// <param name="options">全局选项。</param>
    /// <returns>用户是否确认。</returns>
    public static bool Confirm(string message, GlobalOptions options)
    {
        // 自动确认优先
        if (options.AutoConfirm)
        {
            return true;
        }

        // 非交互模式：不允许弹确认（需要显式 --yes 才能通过）
        if (options.NonInteractive)
        {
            return false;
        }

        // 标准输入不可用时（管道/重定向）无法交互确认
        if (Console.IsInputRedirected)
        {
            return false;
        }

        Console.Write($"{message} [y/N]: ");

        try
        {
            var response = Console.ReadLine();
            return string.Equals(response?.Trim(), "y", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(response?.Trim(), "yes", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 请求用户确认（带默认选项）。
    /// </summary>
    public static bool ConfirmDestructive(string operation, GlobalOptions options)
    {
        return Confirm($"⚠️  确认执行 '{operation}'? 此操作可能影响系统状态。", options);
    }
}
