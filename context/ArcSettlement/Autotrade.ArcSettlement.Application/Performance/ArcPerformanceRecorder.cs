using System.Security.Cryptography;
using System.Text;
using Autotrade.ArcSettlement.Application.Configuration;
using Autotrade.ArcSettlement.Application.Contract.Configuration;
using Autotrade.ArcSettlement.Application.Contract.Proofs;
using Autotrade.ArcSettlement.Application.Contract.Performance;
using Autotrade.ArcSettlement.Application.Contract.Signals;
using Autotrade.ArcSettlement.Application.Proofs;
using Autotrade.ArcSettlement.Application.Signals;
using Microsoft.Extensions.Options;

namespace Autotrade.ArcSettlement.Application.Performance;

public interface IArcPerformanceOutcomeStore
{
    Task<ArcPerformanceOutcomeRecord?> GetBySignalIdAsync(
        string signalId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ArcPerformanceOutcomeRecord>> ListAsync(
        int limit,
        CancellationToken cancellationToken = default);

    Task UpsertAsync(
        ArcPerformanceOutcomeRecord record,
        CancellationToken cancellationToken = default);
}

public interface IArcPerformanceLedgerPublisher
{
    Task<ArcPerformanceLedgerPublishResult> PublishAsync(
        ArcPerformanceLedgerPublishPayload payload,
        CancellationToken cancellationToken = default);
}

public sealed record ArcPerformanceLedgerPublishPayload(
    string SignalId,
    ArcPerformanceLedgerOutcomeStatus Status,
    decimal? RealizedPnlBps,
    decimal? SlippageBps,
    string OutcomeHash);

public enum ArcPerformanceLedgerOutcomeStatus
{
    Executed = 1,
    Rejected = 2,
    Expired = 3,
    Skipped = 4,
    Failed = 5,
    Revoked = 6
}

public sealed record ArcPerformanceLedgerPublishResult(
    string TransactionHash,
    bool Confirmed);

public sealed class ArcPerformanceLedgerDuplicateException(string message) : InvalidOperationException(message);

public sealed class ArcPerformanceRecorder(
    IArcPerformanceOutcomeStore outcomeStore,
    IArcSignalPublicationStore signalStore,
    IArcPerformanceLedgerPublisher publisher,
    ArcSettlementOptionsValidator optionsValidator,
    IOptionsMonitor<ArcSettlementOptions> options,
    TimeProvider timeProvider) : IArcPerformanceRecorder
{
    public async Task<ArcPerformanceRecordResult> RecordAsync(
        RecordArcPerformanceOutcomeRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateOutcomeRequest(request);

        var now = timeProvider.GetUtcNow();
        var signalId = NormalizeBytes32Hash(request.SignalId);
        var existing = await outcomeStore.GetBySignalIdAsync(signalId, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return new ArcPerformanceRecordResult(existing with
            {
                RecordStatus = ArcPerformanceRecordStatus.Duplicate,
                ErrorCode = "OUTCOME_ALREADY_RECORDED"
            }, AlreadyRecorded: true);
        }

        var createdAtUtc = request.CreatedAtUtc ?? now;
        var outcomeHash = HashStable(new OutcomeHashMaterial(
            signalId,
            request.ExecutionId.Trim(),
            request.StrategyId.Trim(),
            request.MarketId.Trim(),
            request.Status,
            request.RealizedPnlBps,
            request.SlippageBps,
            request.FillRate,
            request.ReasonCode?.Trim(),
            createdAtUtc));
        var pending = new ArcPerformanceOutcomeRecord(
            outcomeHash,
            signalId,
            request.ExecutionId.Trim(),
            request.StrategyId.Trim(),
            request.MarketId.Trim(),
            request.Status,
            request.RealizedPnlBps,
            request.SlippageBps,
            request.FillRate,
            request.ReasonCode?.Trim(),
            outcomeHash,
            TransactionHash: null,
            ExplorerUrl: null,
            ArcPerformanceRecordStatus.Pending,
            ErrorCode: null,
            createdAtUtc,
            now);

        await outcomeStore.UpsertAsync(pending, cancellationToken).ConfigureAwait(false);

        var currentOptions = options.CurrentValue;
        if (!currentOptions.Enabled)
        {
            var skipped = pending with
            {
                RecordStatus = ArcPerformanceRecordStatus.SkippedDisabled,
                ErrorCode = "ARC_DISABLED"
            };
            await outcomeStore.UpsertAsync(skipped, cancellationToken).ConfigureAwait(false);
            return new ArcPerformanceRecordResult(skipped, AlreadyRecorded: false);
        }

        var validation = optionsValidator.Validate(currentOptions, ArcSettlementOptionsValidationMode.Write);
        if (!validation.IsValid)
        {
            var failed = pending with
            {
                RecordStatus = ArcPerformanceRecordStatus.Failed,
                ErrorCode = "ARC_CONFIG_INVALID"
            };
            await outcomeStore.UpsertAsync(failed, cancellationToken).ConfigureAwait(false);
            return new ArcPerformanceRecordResult(failed, AlreadyRecorded: false);
        }

        try
        {
            var published = await publisher.PublishAsync(
                    new ArcPerformanceLedgerPublishPayload(
                        signalId,
                        MapLedgerStatus(request.Status),
                        request.RealizedPnlBps,
                        request.SlippageBps,
                        outcomeHash),
                    cancellationToken)
                .ConfigureAwait(false);
            var completed = pending with
            {
                RecordStatus = published.Confirmed
                    ? ArcPerformanceRecordStatus.Confirmed
                    : ArcPerformanceRecordStatus.Submitted,
                TransactionHash = published.TransactionHash,
                ExplorerUrl = BuildExplorerUrl(currentOptions.BlockExplorerBaseUrl, published.TransactionHash),
                RecordedAtUtc = now
            };
            await outcomeStore.UpsertAsync(completed, cancellationToken).ConfigureAwait(false);
            return new ArcPerformanceRecordResult(completed, AlreadyRecorded: false);
        }
        catch (ArcPerformanceLedgerDuplicateException)
        {
            var duplicate = pending with
            {
                RecordStatus = ArcPerformanceRecordStatus.Duplicate,
                ErrorCode = "OUTCOME_ALREADY_RECORDED"
            };
            await outcomeStore.UpsertAsync(duplicate, cancellationToken).ConfigureAwait(false);
            return new ArcPerformanceRecordResult(duplicate, AlreadyRecorded: true);
        }
        catch (Exception)
        {
            var failed = pending with
            {
                RecordStatus = ArcPerformanceRecordStatus.Failed,
                ErrorCode = "PERFORMANCE_LEDGER_PUBLISH_FAILED"
            };
            await outcomeStore.UpsertAsync(failed, cancellationToken).ConfigureAwait(false);
            return new ArcPerformanceRecordResult(failed, AlreadyRecorded: false);
        }
    }

    public async Task<ArcAgentReputation> GetAgentReputationAsync(CancellationToken cancellationToken = default)
    {
        var signals = await signalStore.ListAsync(1000, cancellationToken).ConfigureAwait(false);
        var outcomes = await outcomeStore.ListAsync(1000, cancellationToken).ConfigureAwait(false);
        return CalculateReputation("agent", strategyId: null, signals, outcomes);
    }

    public async Task<ArcAgentReputation> GetStrategyReputationAsync(
        string strategyId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(strategyId);

        var signals = (await signalStore.ListAsync(1000, cancellationToken).ConfigureAwait(false))
            .Where(signal => IsSame(signal.StrategyId, strategyId))
            .ToArray();
        var outcomes = (await outcomeStore.ListAsync(1000, cancellationToken).ConfigureAwait(false))
            .Where(outcome => IsSame(outcome.StrategyId, strategyId))
            .ToArray();
        return CalculateReputation("strategy", strategyId.Trim(), signals, outcomes);
    }

    public Task<ArcPerformanceOutcomeRecord?> GetOutcomeAsync(
        string signalId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(signalId);
        return outcomeStore.GetBySignalIdAsync(NormalizeBytes32Hash(signalId), cancellationToken);
    }

    private ArcAgentReputation CalculateReputation(
        string scope,
        string? strategyId,
        IReadOnlyList<ArcSignalPublicationRecord> signals,
        IReadOnlyList<ArcPerformanceOutcomeRecord> outcomes)
    {
        var now = timeProvider.GetUtcNow();
        var signalIds = signals
            .Select(signal => signal.SignalId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var matchingOutcomes = outcomes
            .Where(outcome => signalIds.Contains(outcome.SignalId))
            .ToArray();
        var outcomeSignalIds = matchingOutcomes
            .Select(outcome => outcome.SignalId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var derivedExpiredSignals = signals.Count(signal =>
            !outcomeSignalIds.Contains(signal.SignalId) && signal.ValidUntilUtc <= now);
        var terminalSignals = matchingOutcomes.Length + derivedExpiredSignals;
        var totalSignals = signals.Count;
        var pendingSignals = Math.Max(0, totalSignals - terminalSignals);

        return new ArcAgentReputation(
            scope,
            strategyId,
            totalSignals,
            terminalSignals,
            pendingSignals,
            matchingOutcomes.Count(IsExecuted),
            matchingOutcomes.Count(outcome => outcome.Status == ArcPerformanceOutcomeStatus.Expired) + derivedExpiredSignals,
            matchingOutcomes.Count(IsRejected),
            matchingOutcomes.Count(outcome => outcome.Status == ArcPerformanceOutcomeStatus.SkippedNoAccess),
            matchingOutcomes.Count(outcome => outcome.Status == ArcPerformanceOutcomeStatus.FailedExecution),
            matchingOutcomes.Count(outcome => outcome.Status == ArcPerformanceOutcomeStatus.CancelledOperator),
            matchingOutcomes.Count(outcome => outcome.Status == ArcPerformanceOutcomeStatus.ExecutedWin),
            matchingOutcomes.Count(outcome => outcome.Status == ArcPerformanceOutcomeStatus.ExecutedLoss),
            matchingOutcomes.Count(outcome => outcome.Status == ArcPerformanceOutcomeStatus.ExecutedFlat),
            Average(matchingOutcomes.Where(IsExecuted).Select(outcome => outcome.RealizedPnlBps)),
            Average(matchingOutcomes.Where(IsExecuted).Select(outcome => outcome.SlippageBps)),
            terminalSignals == 0
                ? 0m
                : Decimal.Divide(matchingOutcomes.Count(outcome => outcome.Status == ArcPerformanceOutcomeStatus.RejectedRisk), terminalSignals),
            totalSignals == 0
                ? 0m
                : Decimal.Divide(terminalSignals, totalSignals),
            now);
    }

    private static void ValidateOutcomeRequest(RecordArcPerformanceOutcomeRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SignalId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.StrategyId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.MarketId);

        if (request.FillRate is < 0m or > 1m)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "FillRate must be between 0 and 1.");
        }

        if (request.Status == ArcPerformanceOutcomeStatus.ExecutedWin
            && request.RealizedPnlBps is not > 0m)
        {
            throw new ArgumentException("ExecutedWin requires positive realized pnl bps.", nameof(request));
        }

        if (request.Status == ArcPerformanceOutcomeStatus.ExecutedLoss
            && request.RealizedPnlBps is not < 0m)
        {
            throw new ArgumentException("ExecutedLoss requires negative realized pnl bps.", nameof(request));
        }

        if (request.Status == ArcPerformanceOutcomeStatus.ExecutedFlat
            && request.RealizedPnlBps is not 0m)
        {
            throw new ArgumentException("ExecutedFlat requires zero realized pnl bps.", nameof(request));
        }
    }

    private static ArcPerformanceLedgerOutcomeStatus MapLedgerStatus(ArcPerformanceOutcomeStatus status)
        => status switch
        {
            ArcPerformanceOutcomeStatus.ExecutedWin or
                ArcPerformanceOutcomeStatus.ExecutedLoss or
                ArcPerformanceOutcomeStatus.ExecutedFlat => ArcPerformanceLedgerOutcomeStatus.Executed,
            ArcPerformanceOutcomeStatus.RejectedRisk or
                ArcPerformanceOutcomeStatus.RejectedCompliance => ArcPerformanceLedgerOutcomeStatus.Rejected,
            ArcPerformanceOutcomeStatus.Expired => ArcPerformanceLedgerOutcomeStatus.Expired,
            ArcPerformanceOutcomeStatus.SkippedNoAccess => ArcPerformanceLedgerOutcomeStatus.Skipped,
            ArcPerformanceOutcomeStatus.FailedExecution => ArcPerformanceLedgerOutcomeStatus.Failed,
            ArcPerformanceOutcomeStatus.CancelledOperator => ArcPerformanceLedgerOutcomeStatus.Revoked,
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown performance outcome status.")
        };

    private static bool IsExecuted(ArcPerformanceOutcomeRecord outcome)
        => outcome.Status is ArcPerformanceOutcomeStatus.ExecutedWin
            or ArcPerformanceOutcomeStatus.ExecutedLoss
            or ArcPerformanceOutcomeStatus.ExecutedFlat;

    private static bool IsRejected(ArcPerformanceOutcomeRecord outcome)
        => outcome.Status is ArcPerformanceOutcomeStatus.RejectedRisk
            or ArcPerformanceOutcomeStatus.RejectedCompliance;

    private static decimal? Average(IEnumerable<decimal?> values)
    {
        var materialized = values
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .ToArray();
        return materialized.Length == 0 ? null : materialized.Average();
    }

    private static string? BuildExplorerUrl(string baseUrl, string transactionHash)
        => string.IsNullOrWhiteSpace(baseUrl)
            ? null
            : $"{baseUrl.TrimEnd('/')}/tx/{transactionHash}";

    private static string NormalizeBytes32Hash(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? value.ToLowerInvariant()
            : $"0x{value.ToLowerInvariant()}";
        if (normalized.Length != 66 || !normalized[2..].All(Uri.IsHexDigit))
        {
            throw new ArgumentException("Signal id must be a 32-byte hex hash.", nameof(value));
        }

        return normalized;
    }

    private static string HashStable<T>(T value)
    {
        var json = ArcProofJson.SerializeStable(value);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return $"0x{Convert.ToHexString(bytes).ToLowerInvariant()}";
    }

    private static bool IsSame(string left, string right)
        => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private sealed record OutcomeHashMaterial(
        string SignalId,
        string ExecutionId,
        string StrategyId,
        string MarketId,
        ArcPerformanceOutcomeStatus Status,
        decimal? RealizedPnlBps,
        decimal? SlippageBps,
        decimal? FillRate,
        string? ReasonCode,
        DateTimeOffset CreatedAtUtc);
}
