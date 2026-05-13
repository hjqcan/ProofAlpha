using System.Text.Json;
using System.Text.Json.Serialization;
using Autotrade.Cli.Infrastructure;
using Autotrade.OpportunityDiscovery.Application.Contract;
using Autotrade.OpportunityDiscovery.Domain.Shared.Enums;
using Microsoft.Extensions.DependencyInjection;

namespace Autotrade.Cli.Commands;

public static class OpportunityCommands
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

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

    public static async Task<int> IngestMessageAsync(
        CommandContext context,
        string sourceName,
        string title,
        string message,
        string? url,
        DateTimeOffset? publishedAtUtc,
        string actor,
        decimal sourceQuality)
    {
        using var scope = context.Host.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IOpportunityDiscoveryService>();
        var result = await service.IngestUserMessageAsync(
                new OpportunityUserMessageIngestionRequest(
                    sourceName,
                    title,
                    message,
                    url,
                    publishedAtUtc,
                    ResolveActor(actor),
                    sourceQuality),
                CancellationToken.None)
            .ConfigureAwait(false);

        OutputFormatter.WriteSuccess("User-provided opportunity evidence ingested.", result, context.GlobalOptions);
        return ExitCodes.Success;
    }

    public static async Task<int> IngestAccountActivityAsync(
        CommandContext context,
        FileInfo input,
        string actor)
    {
        if (input is null || !input.Exists)
        {
            OutputFormatter.WriteError(
                $"Account activity input file was not found: {input?.FullName ?? "<null>"}",
                "VALIDATION_FAILED",
                context.GlobalOptions,
                ExitCodes.ValidationFailed);
            return ExitCodes.ValidationFailed;
        }

        OpportunityAccountActivityIngestionRequest? request;
        try
        {
            var json = await File.ReadAllTextAsync(input.FullName, CancellationToken.None).ConfigureAwait(false);
            request = JsonSerializer.Deserialize<OpportunityAccountActivityIngestionRequest>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            OutputFormatter.WriteError(
                $"Account activity input JSON is invalid: {ex.Message}",
                "VALIDATION_FAILED",
                context.GlobalOptions,
                ExitCodes.ValidationFailed);
            return ExitCodes.ValidationFailed;
        }
        catch (IOException ex)
        {
            OutputFormatter.WriteError(
                $"Account activity input file could not be read: {ex.Message}",
                "VALIDATION_FAILED",
                context.GlobalOptions,
                ExitCodes.ValidationFailed);
            return ExitCodes.ValidationFailed;
        }
        catch (UnauthorizedAccessException ex)
        {
            OutputFormatter.WriteError(
                $"Account activity input file could not be read: {ex.Message}",
                "VALIDATION_FAILED",
                context.GlobalOptions,
                ExitCodes.ValidationFailed);
            return ExitCodes.ValidationFailed;
        }

        if (request is null)
        {
            OutputFormatter.WriteError(
                "Account activity input JSON was empty.",
                "VALIDATION_FAILED",
                context.GlobalOptions,
                ExitCodes.ValidationFailed);
            return ExitCodes.ValidationFailed;
        }

        using var scope = context.Host.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IOpportunityDiscoveryService>();
        var result = await service.IngestPolymarketAccountActivityAsync(
                request with { Actor = ResolveActor(actor) },
                CancellationToken.None)
            .ConfigureAwait(false);

        OutputFormatter.WriteSuccess("Polymarket account activity evidence ingested.", result, context.GlobalOptions);
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

    public static async Task<int> ScoreAsync(CommandContext context, Guid opportunityId)
    {
        using var scope = context.Host.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IOpportunityOperatorService>();
        var result = await service.GetScoreAsync(opportunityId, CancellationToken.None).ConfigureAwait(false);
        OutputFormatter.WriteSuccess("Opportunity score.", result, context.GlobalOptions);
        return result.Hypothesis is null ? ExitCodes.NotFound : ExitCodes.Success;
    }

    public static async Task<int> ReplayAsync(CommandContext context, Guid opportunityId)
    {
        using var scope = context.Host.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IOpportunityOperatorService>();
        var result = await service.GetReplayAsync(opportunityId, CancellationToken.None).ConfigureAwait(false);
        OutputFormatter.WriteSuccess("Opportunity replay status.", result, context.GlobalOptions);
        return result.Hypothesis is null ? ExitCodes.NotFound : ExitCodes.Success;
    }

    public static async Task<int> PromoteAsync(
        CommandContext context,
        Guid opportunityId,
        string actor,
        string? reason)
    {
        if (!ConfirmationService.ConfirmDestructive($"opportunity promote --id {opportunityId}", context.GlobalOptions))
        {
            OutputFormatter.WriteError("Operation cancelled.", "CANCELLED", context.GlobalOptions, ExitCodes.UserCancelled);
            return ExitCodes.UserCancelled;
        }

        using var scope = context.Host.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IOpportunityOperatorService>();
        var result = await service.PromoteAsync(
                new OpportunityPromoteRequest(
                    opportunityId,
                    ResolveActor(actor),
                    string.IsNullOrWhiteSpace(reason) ? "operator promotion" : reason.Trim()),
                CancellationToken.None)
            .ConfigureAwait(false);

        OutputFormatter.WriteSuccess("Opportunity promotion evaluated.", result, context.GlobalOptions);
        return result.Accepted ? ExitCodes.Success : ExitCodes.ValidationFailed;
    }

    public static async Task<int> LiveStatusAsync(CommandContext context)
    {
        using var scope = context.Host.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IOpportunityOperatorService>();
        var result = await service.GetLiveStatusAsync(CancellationToken.None).ConfigureAwait(false);
        OutputFormatter.WriteSuccess("Opportunity Live status.", result, context.GlobalOptions);
        return ExitCodes.Success;
    }

    public static async Task<int> SuspendAsync(
        CommandContext context,
        Guid opportunityId,
        string actor,
        string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            OutputFormatter.WriteError("Suspension reason is required.", "VALIDATION_FAILED", context.GlobalOptions, ExitCodes.ValidationFailed);
            return ExitCodes.ValidationFailed;
        }

        if (!ConfirmationService.ConfirmDestructive($"opportunity suspend --id {opportunityId}", context.GlobalOptions))
        {
            OutputFormatter.WriteError("Operation cancelled.", "CANCELLED", context.GlobalOptions, ExitCodes.UserCancelled);
            return ExitCodes.UserCancelled;
        }

        using var scope = context.Host.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IOpportunityOperatorService>();
        var result = await service.SuspendAsync(
                new OpportunityOperatorSuspendRequest(
                    opportunityId,
                    ResolveActor(actor),
                    reason.Trim(),
                    StrategyId: "llm_opportunity"),
                CancellationToken.None)
            .ConfigureAwait(false);

        OutputFormatter.WriteSuccess("Opportunity suspension evaluated.", result, context.GlobalOptions);
        return result.Suspended ? ExitCodes.Success : ExitCodes.ValidationFailed;
    }

    public static async Task<int> ExplainAsync(CommandContext context, Guid opportunityId)
    {
        using var scope = context.Host.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IOpportunityOperatorService>();
        var result = await service.ExplainAsync(opportunityId, null, CancellationToken.None).ConfigureAwait(false);
        OutputFormatter.WriteSuccess("Opportunity explanation.", result, context.GlobalOptions);
        return result.Hypothesis is null ? ExitCodes.NotFound : ExitCodes.Success;
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
