// ============================================================================
// Strategy 命令处理器
// ============================================================================

using System.Text.Json;
using Autotrade.Cli.Config;
using Autotrade.Cli.Infrastructure;
using Autotrade.Strategy.Application.Engine;
using Microsoft.Extensions.DependencyInjection;

namespace Autotrade.Cli.Commands;

/// <summary>
/// 处理 strategy 相关命令：list、enable、disable。
/// </summary>
public static class StrategyCommands
{
    /// <summary>
    /// 列出所有已注册策略及其状态。
    /// </summary>
    public static async Task<int> ListAsync(CommandContext context)
    {
        using var scope = context.Host.Services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<IStrategyManager>();
        var stateRepository = scope.ServiceProvider.GetRequiredService<IStrategyRunStateRepository>();

        var descriptors = manager.GetRegisteredStrategies();
        var states = await stateRepository.GetAllAsync().ConfigureAwait(false);

        var items = descriptors.Select(d =>
        {
            var state = states.FirstOrDefault(s => string.Equals(s.StrategyId, d.StrategyId, StringComparison.OrdinalIgnoreCase));
            return new
            {
                d.StrategyId,
                d.Name,
                d.Enabled,
                d.ConfigVersion,
                d.OptionsSectionName,
                State = state?.State.ToString() ?? "Unknown",
                state?.LastError
            };
        }).ToList();

        if (context.JsonOutput)
        {
            Console.WriteLine(JsonSerializer.Serialize(items, new JsonSerializerOptions { WriteIndented = true }));
        }
        else
        {
            foreach (var item in items)
            {
                Console.WriteLine($"- {item.StrategyId} ({item.Name}) Enabled={item.Enabled} State={item.State}");
            }
        }

        return 0;
    }

    /// <summary>
    /// 设置策略的启用/禁用状态。
    /// </summary>
    public static async Task<int> SetEnabledAsync(
        CommandContext context, 
        string strategyId, 
        bool enabled, 
        ConfigFileService configService)
    {
        using var scope = context.Host.Services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<IStrategyManager>();
        var descriptor = manager.GetRegisteredStrategies()
            .FirstOrDefault(d => string.Equals(d.StrategyId, strategyId, StringComparison.OrdinalIgnoreCase));

        if (descriptor is null)
        {
            Console.Error.WriteLine("未知策略 ID");
            return ExitCodes.NotFound;
        }

        if (!ConfirmationService.ConfirmDestructive($"strategy {(enabled ? "enable" : "disable")} --id {strategyId}", context.GlobalOptions))
        {
            Console.WriteLine("操作已取消。");
            return ExitCodes.UserCancelled;
        }

        var path = $"{descriptor.OptionsSectionName}:Enabled";
        configService.SetValue(path, enabled.ToString());

        Console.WriteLine($"{strategyId} 已设置为 Enabled={enabled}（运行中的进程会在数秒内自动生效）。");
        return ExitCodes.Success;
    }
}
