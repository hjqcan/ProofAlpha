using Autotrade.Cli.Infrastructure;
using Autotrade.OpportunityDiscovery.Application.Contract;
using Autotrade.OpportunityDiscovery.Domain.Shared.Enums;
using Microsoft.Extensions.DependencyInjection;

namespace Autotrade.Cli.Commands;

public static class OpportunityCommands
{
    public static async Task<int> ScanAsync(
        CommandContext context,
        decimal minVolume24h,
        decimal minLiquidity,
        int maxMarkets)
    {
        using var scope = context.Host.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IOpportunityDiscoveryService>();
        var result = await service.ScanAsync(
                new OpportunityScanRequest("cli", minVolume24h, minLiquidity, maxMarkets),
                CancellationToken.None)
            .ConfigureAwait(false);

        OutputFormatter.WriteSuccess("Opportunity scan completed.", result, context.GlobalOptions);
        return ExitCodes.Success;
    }

    public static async Task<int> ListAsync(
        CommandContext context,
        string? status,
        int limit)
    {
        if (!TryParseStatus(status, context, out var parsedStatus))
        {
            return ExitCodes.ValidationFailed;
        }

        using var scope = context.Host.Services.CreateScope();
        var query = scope.ServiceProvider.GetRequiredService<IOpportunityQueryService>();
        var opportunities = await query.ListOpportunitiesAsync(parsedStatus, limit, CancellationToken.None)
            .ConfigureAwait(false);

        OutputFormatter.WriteSuccess("Market opportunities.", opportunities, context.GlobalOptions);
        return ExitCodes.Success;
    }

    public static async Task<int> ShowAsync(CommandContext context, Guid opportunityId)
    {
        using var scope = context.Host.Services.CreateScope();
        var query = scope.ServiceProvider.GetRequiredService<IOpportunityQueryService>();
        var opportunity = await query.GetOpportunityAsync(opportunityId, CancellationToken.None)
            .ConfigureAwait(false);
        if (opportunity is null)
        {
            OutputFormatter.WriteError(
                $"Opportunity {opportunityId} was not found.",
                "NOT_FOUND",
                context.GlobalOptions,
                ExitCodes.NotFound);
            return ExitCodes.NotFound;
        }

        var evidence = await query.GetEvidenceAsync(opportunityId, CancellationToken.None)
            .ConfigureAwait(false);
        OutputFormatter.WriteSuccess("Market opportunity.", new { opportunity, evidence }, context.GlobalOptions);
        return ExitCodes.Success;
    }

    public static Task<int> ApproveAsync(
        CommandContext context,
        Guid opportunityId,
        string actor,
        string? notes)
    {
        return ReviewAsync(
            context,
            "opportunity approve",
            opportunityId,
            actor,
            notes,
            (service, request) => service.ApproveAsync(request, CancellationToken.None),
            "Opportunity approved.");
    }

    public static Task<int> RejectAsync(
        CommandContext context,
        Guid opportunityId,
        string actor,
        string? notes)
    {
        return ReviewAsync(
            context,
            "opportunity reject",
            opportunityId,
            actor,
            notes,
            (service, request) => service.RejectAsync(request, CancellationToken.None),
            "Opportunity rejected.");
    }

    public static Task<int> PublishAsync(
        CommandContext context,
        Guid opportunityId,
        string actor,
        string? notes)
    {
        return ReviewAsync(
            context,
            "opportunity publish",
            opportunityId,
            actor,
            notes,
            (service, request) => service.PublishAsync(request, CancellationToken.None),
            "Opportunity published.");
    }

    private static async Task<int> ReviewAsync(
        CommandContext context,
        string operation,
        Guid opportunityId,
        string actor,
        string? notes,
        Func<IOpportunityDiscoveryService, OpportunityReviewRequest, Task<MarketOpportunityDto>> action,
        string successMessage)
    {
        if (opportunityId == Guid.Empty)
        {
            OutputFormatter.WriteError("Opportunity id is required.", "VALIDATION_FAILED", context.GlobalOptions, ExitCodes.ValidationFailed);
            return ExitCodes.ValidationFailed;
        }

        if (!ConfirmationService.ConfirmDestructive($"{operation} --id {opportunityId}", context.GlobalOptions))
        {
            OutputFormatter.WriteError("Operation cancelled.", "CANCELLED", context.GlobalOptions, ExitCodes.UserCancelled);
            return ExitCodes.UserCancelled;
        }

        using var scope = context.Host.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IOpportunityDiscoveryService>();
        var result = await action(
                service,
                new OpportunityReviewRequest(opportunityId, ResolveActor(actor), notes))
            .ConfigureAwait(false);

        OutputFormatter.WriteSuccess(successMessage, result, context.GlobalOptions);
        return ExitCodes.Success;
    }

    private static bool TryParseStatus(
        string? status,
        CommandContext context,
        out OpportunityStatus? parsedStatus)
    {
        parsedStatus = null;
        if (string.IsNullOrWhiteSpace(status))
        {
            return true;
        }

        if (Enum.TryParse<OpportunityStatus>(status, ignoreCase: true, out var parsed))
        {
            parsedStatus = parsed;
            return true;
        }

        OutputFormatter.WriteError(
            $"Invalid opportunity status: {status}",
            "VALIDATION_FAILED",
            context.GlobalOptions,
            ExitCodes.ValidationFailed);
        return false;
    }

    private static string ResolveActor(string actor)
    {
        if (!string.IsNullOrWhiteSpace(actor))
        {
            return actor.Trim();
        }

        return string.IsNullOrWhiteSpace(Environment.UserName) ? "cli" : Environment.UserName;
    }
}
