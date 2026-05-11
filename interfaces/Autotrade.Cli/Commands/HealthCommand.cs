// ============================================================================
// Health 命令处理器
// ============================================================================

using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Autotrade.Cli.Commands;

/// <summary>
/// 处理 health 命令：执行健康检查。
/// </summary>
public static class HealthCommand
{
    /// <summary>
    /// 执行 health 命令。
    /// </summary>
    /// <param name="context">命令上下文。</param>
    /// <param name="mode">"liveness" 或 "readiness"。</param>
    /// <returns>0=健康，2=不健康。</returns>
    public static async Task<int> ExecuteAsync(CommandContext context, string mode)
    {
        using var scope = context.Host.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<HealthCheckService>();

        // 根据模式选择健康检查标签
        var tag = mode.Equals("liveness", StringComparison.OrdinalIgnoreCase) ? "live" : "ready";
        var report = await service.CheckHealthAsync(entry => entry.Tags.Contains(tag)).ConfigureAwait(false);

        if (context.JsonOutput)
        {
            var output = new
            {
                Status = report.Status.ToString(),
                Entries = report.Entries.ToDictionary(
                    kvp => kvp.Key,
                    kvp => new { Status = kvp.Value.Status.ToString(), kvp.Value.Description })
            };
            Console.WriteLine(JsonSerializer.Serialize(output, new JsonSerializerOptions { WriteIndented = true }));
        }
        else
        {
            Console.WriteLine($"Health ({mode}): {report.Status}");
            foreach (var entry in report.Entries)
            {
                Console.WriteLine($"- {entry.Key}: {entry.Value.Status} {entry.Value.Description}");
            }
        }

        return report.Status == HealthStatus.Healthy ? 0 : 2;
    }
}
