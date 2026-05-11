// ============================================================================
// 审计日志服务
// ============================================================================

using System.Diagnostics;
using System.Text.Json;
using Autotrade.Strategy.Application.Audit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Autotrade.Cli.Infrastructure;

/// <summary>
/// 命令执行审计服务。
/// </summary>
public static class CommandAuditService
{
    private static readonly TimeSpan AuditWriteTimeout = TimeSpan.FromSeconds(2);

    /// <summary>
    /// 执行命令并记录审计日志。
    /// 所有命令都通过此方法执行，确保统一的错误处理和审计追踪。
    /// </summary>
    /// <param name="commandName">命令名称。</param>
    /// <param name="arguments">命令参数。</param>
    /// <param name="configPath">配置文件路径。</param>
    /// <param name="action">实际执行的操作。</param>
    /// <returns>退出码（0=成功，非 0=失败）。</returns>
    public static async Task<int> ExecuteWithAuditAsync(
        string commandName,
        object arguments,
        string? configPath,
        Func<IHost, Task<int>> action)
    {
        var sw = Stopwatch.StartNew();
        using var host = HostBuilderExtensions.BuildAutotradeHost(
            Environment.GetCommandLineArgs(), 
            configPath);
        var exitCode = 0;

        try
        {
            exitCode = await action(host).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            exitCode = 1;
        }
        finally
        {
            sw.Stop();
            // 记录审计日志
            await LogAuditAsync(host, commandName, arguments, exitCode, sw.ElapsedMilliseconds)
                .ConfigureAwait(false);
        }

        return exitCode;
    }

    /// <summary>
    /// Execute a read-only CLI command with the trading host but without command audit side effects.
    /// </summary>
    public static async Task<int> ExecuteWithHostAsync(
        string? configPath,
        Func<IHost, Task<int>> action,
        bool suppressConsoleLogs = false)
    {
        using var host = HostBuilderExtensions.BuildAutotradeHost(
            Environment.GetCommandLineArgs(),
            configPath,
            suppressConsoleLogs);

        try
        {
            return await action(host).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    /// <summary>
    /// Execute a local CLI command that must not construct the trading host.
    /// Config commands use this path so validation remains available before the database exists.
    /// </summary>
    public static async Task<int> ExecuteLocalAsync(Func<Task<int>> action)
    {
        try
        {
            return await action().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    /// <summary>
    /// 将命令执行记录写入审计日志。
    /// </summary>
    private static async Task LogAuditAsync(
        IHost host, 
        string commandName, 
        object arguments, 
        int exitCode, 
        long durationMs)
    {
        try
        {
            using var scope = host.Services.CreateScope();
            var auditLogger = scope.ServiceProvider.GetService<ICommandAuditLogger>();
            if (auditLogger is null)
            {
                return;
            }

            var entry = new CommandAuditEntry(
                commandName,
                JsonSerializer.Serialize(arguments),
                Environment.UserName,
                exitCode == 0,
                exitCode,
                durationMs,
                DateTimeOffset.UtcNow);

            using var timeoutCts = new CancellationTokenSource(AuditWriteTimeout);
            await auditLogger.LogAsync(entry, timeoutCts.Token).ConfigureAwait(false);
        }
        catch
        {
            // 忽略审计日志失败，不影响主流程
        }
    }
}
