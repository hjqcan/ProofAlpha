using Autotrade.ArcSettlement.Application.Contract.Revenue;
using Microsoft.Extensions.DependencyInjection;

namespace Autotrade.Cli.Commands;

public static class ArcRevenueCommands
{
    public static async Task<int> RecordAsync(
        CommandContext context,
        string? settlementId,
        string sourceKind,
        string signalId,
        string? executionId,
        string walletAddress,
        string strategyId,
        decimal grossUsdc,
        string? tokenAddress,
        bool simulated,
        string? sourceTransactionHash,
        string reason,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.TryParse<ArcRevenueSourceKind>(sourceKind, ignoreCase: true, out var parsedSourceKind))
        {
            OutputFormatter.WriteError("source-kind must be a valid ArcRevenueSourceKind.", "INVALID_REVENUE_SOURCE_KIND", context.GlobalOptions, ExitCodes.ValidationFailed);
            return ExitCodes.ValidationFailed;
        }

        var service = context.Services.GetRequiredService<IArcRevenueSettlementRecorder>();
        try
        {
            var result = await service.RecordAsync(
                    new ArcRevenueSettlementRequest(
                        settlementId,
                        parsedSourceKind,
                        signalId,
                        executionId,
                        walletAddress,
                        strategyId,
                        grossUsdc,
                        tokenAddress,
                        Shares: null,
                        reason,
                        simulated,
                        sourceTransactionHash),
                    cancellationToken)
                .ConfigureAwait(false);

            OutputFormatter.WriteSuccess("Arc revenue settlement recorded.", result, context.GlobalOptions);
            return ExitCodes.Success;
        }
        catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException)
        {
            OutputFormatter.WriteError(ex.Message, "INVALID_REVENUE_SETTLEMENT", context.GlobalOptions, ExitCodes.ValidationFailed);
            return ExitCodes.ValidationFailed;
        }
    }

    public static async Task<int> ListAsync(
        CommandContext context,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var service = context.Services.GetRequiredService<IArcRevenueSettlementRecorder>();
        var records = await service.ListAsync(limit, cancellationToken)
            .ConfigureAwait(false);
        OutputFormatter.WriteSuccess("Arc revenue settlements.", records, context.GlobalOptions);
        return ExitCodes.Success;
    }

    public static async Task<int> ShowAsync(
        CommandContext context,
        string settlementId,
        CancellationToken cancellationToken = default)
    {
        var service = context.Services.GetRequiredService<IArcRevenueSettlementRecorder>();
        var record = await service.GetAsync(settlementId, cancellationToken)
            .ConfigureAwait(false);
        if (record is null)
        {
            OutputFormatter.WriteError($"Arc revenue settlement not found: {settlementId}", "ARC_REVENUE_SETTLEMENT_NOT_FOUND", context.GlobalOptions, ExitCodes.NotFound);
            return ExitCodes.NotFound;
        }

        OutputFormatter.WriteSuccess("Arc revenue settlement.", record, context.GlobalOptions);
        return ExitCodes.Success;
    }
}
