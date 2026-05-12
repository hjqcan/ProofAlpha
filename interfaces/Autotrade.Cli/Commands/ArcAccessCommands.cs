using Autotrade.ArcSettlement.Application.Contract.Access;
using Microsoft.Extensions.DependencyInjection;

namespace Autotrade.Cli.Commands;

public static class ArcAccessCommands
{
    public static Task<int> PlansAsync(CommandContext context)
    {
        var service = context.Services.GetRequiredService<IArcSubscriptionPlanService>();
        OutputFormatter.WriteSuccess("Arc subscription plans.", service.ListPlans(), context.GlobalOptions);
        return Task.FromResult(ExitCodes.Success);
    }

    public static async Task<int> StatusAsync(
        CommandContext context,
        string walletAddress,
        string strategyKey,
        CancellationToken cancellationToken = default)
    {
        var reader = context.Services.GetRequiredService<IArcStrategyAccessReader>();
        var status = await reader.GetAccessAsync(walletAddress, strategyKey, cancellationToken)
            .ConfigureAwait(false);
        OutputFormatter.WriteSuccess("Arc access status.", status, context.GlobalOptions);
        return ExitCodes.Success;
    }

    public static async Task<int> SyncAsync(
        CommandContext context,
        string walletAddress,
        string strategyKey,
        int planId,
        string transactionHash,
        DateTimeOffset expiresAtUtc,
        long? blockNumber,
        CancellationToken cancellationToken = default)
    {
        var service = context.Services.GetRequiredService<IArcSubscriptionSyncService>();
        try
        {
            var result = await service.SyncAsync(
                    new SyncArcAccessRequest(
                        walletAddress,
                        strategyKey,
                        planId,
                        transactionHash,
                        expiresAtUtc,
                        blockNumber),
                    cancellationToken)
                .ConfigureAwait(false);

            OutputFormatter.WriteSuccess("Arc access mirror synced.", result, context.GlobalOptions);
            return ExitCodes.Success;
        }
        catch (ArcSubscriptionSyncException ex)
        {
            var exitCode = string.Equals(ex.ErrorCode, "PLAN_NOT_FOUND", StringComparison.OrdinalIgnoreCase)
                ? ExitCodes.NotFound
                : ExitCodes.ValidationFailed;
            OutputFormatter.WriteError(ex.Message, ex.ErrorCode, context.GlobalOptions, exitCode);
            return exitCode;
        }
    }
}
