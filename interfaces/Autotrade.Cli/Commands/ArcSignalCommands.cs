using System.Text.Json;
using Autotrade.ArcSettlement.Application.Contract.Proofs;
using Autotrade.ArcSettlement.Application.Contract.Signals;
using Microsoft.Extensions.DependencyInjection;

namespace Autotrade.Cli.Commands;

public static class ArcSignalCommands
{
    public static async Task<int> PublishAsync(
        CommandContext context,
        FileInfo proofFile,
        string source,
        string sourceId,
        string sourceStatus,
        string actor,
        string reason,
        string? sourcePolicyHash,
        CancellationToken cancellationToken = default)
    {
        if (!proofFile.Exists)
        {
            OutputFormatter.WriteError($"Proof file not found: {proofFile.FullName}", "PROOF_FILE_NOT_FOUND", context.GlobalOptions, ExitCodes.NotFound);
            return ExitCodes.NotFound;
        }

        if (!TryParseSourceKind(source, out var sourceKind))
        {
            OutputFormatter.WriteError("source must be 'opportunity' or 'decision'.", "INVALID_SOURCE", context.GlobalOptions, ExitCodes.ValidationFailed);
            return ExitCodes.ValidationFailed;
        }

        if (!Enum.TryParse<ArcSignalSourceReviewStatus>(sourceStatus, ignoreCase: true, out var reviewStatus))
        {
            OutputFormatter.WriteError("status must be a valid ArcSignalSourceReviewStatus.", "INVALID_SOURCE_STATUS", context.GlobalOptions, ExitCodes.ValidationFailed);
            return ExitCodes.ValidationFailed;
        }

        var proofJson = await File.ReadAllTextAsync(proofFile.FullName, cancellationToken).ConfigureAwait(false);
        var proof = JsonSerializer.Deserialize<ArcStrategySignalProofDocument>(
            proofJson,
            ArcProofJson.StableSerializerOptions);
        if (proof is null)
        {
            OutputFormatter.WriteError("Proof file did not contain a valid signal proof document.", "INVALID_PROOF", context.GlobalOptions, ExitCodes.ValidationFailed);
            return ExitCodes.ValidationFailed;
        }

        if (proof.SourceKind != sourceKind || !string.Equals(proof.SourceId, sourceId, StringComparison.OrdinalIgnoreCase))
        {
            OutputFormatter.WriteError("Proof source does not match --source and --id.", "PROOF_SOURCE_MISMATCH", context.GlobalOptions, ExitCodes.ValidationFailed);
            return ExitCodes.ValidationFailed;
        }

        var service = context.Services.GetRequiredService<IArcSignalPublicationService>();
        var result = await service.PublishAsync(
                new PublishArcSignalRequest(proof, reviewStatus, actor, reason, sourcePolicyHash),
                cancellationToken)
            .ConfigureAwait(false);

        OutputFormatter.WriteSuccess("Arc signal publication recorded.", result, context.GlobalOptions);
        return ExitCodes.Success;
    }

    public static async Task<int> ListAsync(
        CommandContext context,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var service = context.Services.GetRequiredService<IArcSignalPublicationService>();
        var records = await service.ListAsync(new ArcSignalPublicationQuery(limit), cancellationToken)
            .ConfigureAwait(false);
        OutputFormatter.WriteSuccess("Arc signal publications.", records, context.GlobalOptions);
        return ExitCodes.Success;
    }

    public static async Task<int> ShowAsync(
        CommandContext context,
        string signalId,
        CancellationToken cancellationToken = default)
    {
        var service = context.Services.GetRequiredService<IArcSignalPublicationService>();
        var record = await service.GetAsync(signalId, cancellationToken).ConfigureAwait(false);
        if (record is null)
        {
            OutputFormatter.WriteError($"Arc signal not found: {signalId}", "ARC_SIGNAL_NOT_FOUND", context.GlobalOptions, ExitCodes.NotFound);
            return ExitCodes.NotFound;
        }

        OutputFormatter.WriteSuccess("Arc signal publication.", record, context.GlobalOptions);
        return ExitCodes.Success;
    }

    private static bool TryParseSourceKind(string source, out ArcProofSourceKind sourceKind)
    {
        if (string.Equals(source, "opportunity", StringComparison.OrdinalIgnoreCase))
        {
            sourceKind = ArcProofSourceKind.Opportunity;
            return true;
        }

        if (string.Equals(source, "decision", StringComparison.OrdinalIgnoreCase))
        {
            sourceKind = ArcProofSourceKind.StrategyDecision;
            return true;
        }

        sourceKind = default;
        return false;
    }
}
