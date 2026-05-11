using Autotrade.Cli.Config;
using Autotrade.Cli.Infrastructure;
using Autotrade.Trading.Application.Contract.Risk;

namespace Autotrade.Cli.Commands;

public static class KillSwitchCommands
{
    public static int Activate(
        CommandContext ctx,
        ConfigFileService configService,
        string? strategyId,
        string level,
        string reasonCode,
        string reason,
        string? marketId,
        string? contextJson)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(configService);

        var parsedLevel = ParseLevel(level);
        if (parsedLevel is null || parsedLevel is KillSwitchLevel.None)
        {
            OutputFormatter.WriteError(
                $"无效 level: {level}（支持 soft/hard 或 SoftStop/HardStop）",
                "VALIDATION_FAILED",
                ctx.GlobalOptions,
                ExitCodes.ValidationFailed);
            return ExitCodes.ValidationFailed;
        }

        var levelValue = parsedLevel.Value;

        if (string.IsNullOrWhiteSpace(reason))
        {
            OutputFormatter.WriteError("reason 不能为空", "VALIDATION_FAILED", ctx.GlobalOptions, ExitCodes.ValidationFailed);
            return ExitCodes.ValidationFailed;
        }

        reasonCode = string.IsNullOrWhiteSpace(reasonCode) ? "MANUAL" : reasonCode.Trim();

        var scope = string.IsNullOrWhiteSpace(strategyId) ? "global" : $"strategy:{strategyId}";
        var requiresConfirm = levelValue == KillSwitchLevel.HardStop;
        if (requiresConfirm)
        {
            if (!ConfirmationService.ConfirmDestructive($"killswitch activate --scope {scope} --level {levelValue}", ctx.GlobalOptions))
            {
                OutputFormatter.WriteError("操作已取消", "USER_CANCELLED", ctx.GlobalOptions, ExitCodes.UserCancelled);
                return ExitCodes.UserCancelled;
            }
        }

        if (string.IsNullOrWhiteSpace(strategyId))
        {
            configService.SetValue("RiskControl:KillSwitch:GlobalActive", "true");
            configService.SetValue("RiskControl:KillSwitch:GlobalLevel", levelValue.ToString());
            configService.SetValue("RiskControl:KillSwitch:GlobalReasonCode", reasonCode);
            configService.SetValue("RiskControl:KillSwitch:GlobalReason", reason);
            if (!string.IsNullOrWhiteSpace(contextJson))
            {
                configService.SetValue("RiskControl:KillSwitch:GlobalContextJson", contextJson!);
            }

            OutputFormatter.WriteSuccess(
                $"已激活全局 Kill Switch（{levelValue}）",
                new { scope = "global", level = levelValue.ToString(), reasonCode, reason },
                ctx.GlobalOptions);
            return ExitCodes.Success;
        }

        var sid = strategyId.Trim();
        configService.SetValue($"RiskControl:KillSwitch:Strategies:{sid}:Active", "true");
        configService.SetValue($"RiskControl:KillSwitch:Strategies:{sid}:Level", levelValue.ToString());
        configService.SetValue($"RiskControl:KillSwitch:Strategies:{sid}:ReasonCode", reasonCode);
        configService.SetValue($"RiskControl:KillSwitch:Strategies:{sid}:Reason", reason);
        if (!string.IsNullOrWhiteSpace(marketId))
        {
            configService.SetValue($"RiskControl:KillSwitch:Strategies:{sid}:MarketId", marketId!);
        }
        if (!string.IsNullOrWhiteSpace(contextJson))
        {
            configService.SetValue($"RiskControl:KillSwitch:Strategies:{sid}:ContextJson", contextJson!);
        }

        OutputFormatter.WriteSuccess(
            $"已激活策略 Kill Switch（{levelValue}）: {sid}",
            new { scope = "strategy", strategyId = sid, level = levelValue.ToString(), reasonCode, reason, marketId },
            ctx.GlobalOptions);
        return ExitCodes.Success;
    }

    public static int Reset(
        CommandContext ctx,
        ConfigFileService configService,
        string? strategyId)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(configService);

        var scope = string.IsNullOrWhiteSpace(strategyId) ? "global" : $"strategy:{strategyId}";
        if (!ConfirmationService.ConfirmDestructive($"killswitch reset --scope {scope}", ctx.GlobalOptions))
        {
            OutputFormatter.WriteError("操作已取消", "USER_CANCELLED", ctx.GlobalOptions, ExitCodes.UserCancelled);
            return ExitCodes.UserCancelled;
        }

        if (string.IsNullOrWhiteSpace(strategyId))
        {
            configService.SetValue("RiskControl:KillSwitch:GlobalActive", "false");
            configService.SetValue("RiskControl:KillSwitch:GlobalResetToken", Guid.NewGuid().ToString("N"));
            OutputFormatter.WriteSuccess(
                "已重置全局 Kill Switch（运行中的进程会自动生效）",
                new { scope = "global" },
                ctx.GlobalOptions);
            return ExitCodes.Success;
        }

        var sid = strategyId.Trim();
        configService.SetValue($"RiskControl:KillSwitch:Strategies:{sid}:Active", "false");
        configService.SetValue($"RiskControl:KillSwitch:Strategies:{sid}:ResetToken", Guid.NewGuid().ToString("N"));
        OutputFormatter.WriteSuccess(
            $"已重置策略 Kill Switch（运行中的进程会自动生效）: {sid}",
            new { scope = "strategy", strategyId = sid },
            ctx.GlobalOptions);
        return ExitCodes.Success;
    }

    private static KillSwitchLevel? ParseLevel(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var v = raw.Trim();
        if (string.Equals(v, "soft", StringComparison.OrdinalIgnoreCase))
        {
            return KillSwitchLevel.SoftStop;
        }

        if (string.Equals(v, "hard", StringComparison.OrdinalIgnoreCase))
        {
            return KillSwitchLevel.HardStop;
        }

        if (Enum.TryParse<KillSwitchLevel>(v, ignoreCase: true, out var parsed))
        {
            return parsed;
        }

        return null;
    }
}

