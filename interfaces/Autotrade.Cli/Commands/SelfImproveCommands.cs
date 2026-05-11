using Autotrade.SelfImprove.Application.Contract;
using Autotrade.SelfImprove.Application.Contract.Episodes;
using Autotrade.SelfImprove.Domain.Shared.Enums;
using Microsoft.Extensions.DependencyInjection;

namespace Autotrade.Cli.Commands;

public static class SelfImproveCommands
{
    public static async Task<int> RunAsync(
        CommandContext context,
        string strategyId,
        string? marketId,
        string? from,
        string? to,
        int windowMinutes)
    {
        var (start, end) = ResolveWindow(from, to, windowMinutes);
        var service = context.Services.GetRequiredService<ISelfImproveService>();
        var result = await service.RunAsync(
            new BuildStrategyEpisodeRequest(strategyId, marketId, start, end),
            "cli",
            CancellationToken.None).ConfigureAwait(false);

        OutputFormatter.WriteSuccess("SelfImprove run completed.", result, context.GlobalOptions);
        return ExitCodes.Success;
    }

    public static async Task<int> ListAsync(CommandContext context, int limit)
    {
        var service = context.Services.GetRequiredService<ISelfImproveService>();
        var runs = await service.ListRunsAsync(limit, CancellationToken.None).ConfigureAwait(false);
        OutputFormatter.WriteSuccess("SelfImprove runs.", runs, context.GlobalOptions);
        return ExitCodes.Success;
    }

    public static async Task<int> ShowAsync(CommandContext context, Guid runId)
    {
        var service = context.Services.GetRequiredService<ISelfImproveService>();
        var run = await service.GetRunAsync(runId, CancellationToken.None).ConfigureAwait(false);
        if (run is null)
        {
            OutputFormatter.WriteError($"SelfImprove run {runId} was not found.", "NOT_FOUND", context.GlobalOptions, ExitCodes.NotFound);
            return ExitCodes.NotFound;
        }

        OutputFormatter.WriteSuccess("SelfImprove run.", run, context.GlobalOptions);
        return ExitCodes.Success;
    }

    public static async Task<int> ApplyAsync(CommandContext context, Guid proposalId, bool dryRun)
    {
        var service = context.Services.GetRequiredService<ISelfImproveService>();
        var outcome = await service.ApplyProposalAsync(
            new ApplyProposalRequest(proposalId, dryRun, "cli"),
            CancellationToken.None).ConfigureAwait(false);
        OutputFormatter.WriteSuccess(dryRun ? "SelfImprove patch dry-run completed." : "SelfImprove patch applied.", outcome, context.GlobalOptions);
        return outcome.SuccessLike() ? ExitCodes.Success : ExitCodes.ValidationFailed;
    }

    public static async Task<int> PromoteAsync(CommandContext context, Guid generatedVersionId, string stage)
    {
        if (!Enum.TryParse<GeneratedStrategyStage>(stage, ignoreCase: true, out var parsed))
        {
            OutputFormatter.WriteError($"Invalid generated strategy stage: {stage}", "VALIDATION_FAILED", context.GlobalOptions, ExitCodes.ValidationFailed);
            return ExitCodes.ValidationFailed;
        }

        var service = context.Services.GetRequiredService<ISelfImproveService>();
        var result = await service.PromoteGeneratedStrategyAsync(generatedVersionId, parsed, CancellationToken.None)
            .ConfigureAwait(false);
        OutputFormatter.WriteSuccess("Generated strategy promoted.", result, context.GlobalOptions);
        return ExitCodes.Success;
    }

    public static async Task<int> RollbackAsync(CommandContext context, Guid generatedVersionId)
    {
        var service = context.Services.GetRequiredService<ISelfImproveService>();
        var result = await service.RollbackGeneratedStrategyAsync(generatedVersionId, CancellationToken.None)
            .ConfigureAwait(false);
        OutputFormatter.WriteSuccess("Generated strategy rolled back.", result, context.GlobalOptions);
        return ExitCodes.Success;
    }

    public static async Task<int> QuarantineAsync(CommandContext context, Guid generatedVersionId, string reason)
    {
        var service = context.Services.GetRequiredService<ISelfImproveService>();
        var result = await service.QuarantineGeneratedStrategyAsync(generatedVersionId, reason, CancellationToken.None)
            .ConfigureAwait(false);
        OutputFormatter.WriteSuccess("Generated strategy quarantined.", result, context.GlobalOptions);
        return ExitCodes.Success;
    }

    private static (DateTimeOffset Start, DateTimeOffset End) ResolveWindow(
        string? from,
        string? to,
        int windowMinutes)
    {
        var end = string.IsNullOrWhiteSpace(to)
            ? DateTimeOffset.UtcNow
            : DateTimeOffset.Parse(to, null, System.Globalization.DateTimeStyles.AssumeUniversal);
        var start = string.IsNullOrWhiteSpace(from)
            ? end.AddMinutes(-Math.Clamp(windowMinutes, 5, 24 * 60))
            : DateTimeOffset.Parse(from, null, System.Globalization.DateTimeStyles.AssumeUniversal);
        if (end <= start)
        {
            throw new ArgumentException("--to must be after --from.");
        }

        return (start, end);
    }

    private static bool SuccessLike(this PatchOutcomeDto outcome)
    {
        return outcome.Status is PatchOutcomeStatus.Applied or PatchOutcomeStatus.DryRunPassed;
    }
}
