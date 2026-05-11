using Autotrade.ArcSettlement.Application.Configuration;
using Autotrade.ArcSettlement.Application.Contract.Configuration;
using Autotrade.ArcSettlement.Application.Contract.Proofs;
using Autotrade.ArcSettlement.Application.Contract.Signals;
using Autotrade.ArcSettlement.Application.Proofs;
using Microsoft.Extensions.Options;

namespace Autotrade.ArcSettlement.Application.Signals;

public interface IArcSignalPublicationStore
{
    Task<ArcSignalPublicationRecord?> GetBySignalIdAsync(
        string signalId,
        CancellationToken cancellationToken = default);

    Task<ArcSignalPublicationRecord?> GetBySourceAsync(
        ArcProofSourceKind sourceKind,
        string sourceId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ArcSignalPublicationRecord>> ListAsync(
        int limit,
        CancellationToken cancellationToken = default);

    Task UpsertAsync(
        ArcSignalPublicationRecord record,
        CancellationToken cancellationToken = default);
}

public interface IArcSignalRegistryPublisher
{
    Task<ArcSignalRegistryPublishResult> PublishAsync(
        ArcSignalRegistryPublishPayload payload,
        CancellationToken cancellationToken = default);
}

public sealed record ArcSignalRegistryPublishPayload(
    string SignalId,
    string AgentAddress,
    string Venue,
    string StrategyKey,
    string ReasoningHash,
    string RiskEnvelopeHash,
    decimal ExpectedEdgeBps,
    decimal MaxNotionalUsdc,
    DateTimeOffset ValidUntilUtc);

public sealed record ArcSignalRegistryPublishResult(
    string TransactionHash,
    bool Confirmed);

public sealed class ArcSignalRegistryDuplicateException(string message) : InvalidOperationException(message);

public sealed class ArcSignalPublicationService(
    IArcSignalPublicationStore store,
    IArcSignalRegistryPublisher publisher,
    IArcProofHashService hashService,
    ArcSettlementOptionsValidator optionsValidator,
    IOptionsMonitor<ArcSettlementOptions> options,
    TimeProvider timeProvider) : IArcSignalPublicationService
{
    public async Task<ArcSignalPublicationResult> PublishAsync(
        PublishArcSignalRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.SignalProof);

        var now = timeProvider.GetUtcNow();
        var signalHash = hashService.HashSignal(request.SignalProof);
        var signalId = NormalizeBytes32Hash(signalHash);

        var existing = await store.GetBySignalIdAsync(signalId, cancellationToken).ConfigureAwait(false)
            ?? await store.GetBySourceAsync(
                request.SignalProof.SourceKind,
                request.SignalProof.SourceId,
                cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return new ArcSignalPublicationResult(existing, AlreadyExisted: true);
        }

        var pending = CreateRecord(
            request,
            signalId,
            signalHash,
            ArcSignalPublicationStatus.Pending,
            transactionHash: null,
            explorerUrl: null,
            errorCode: null,
            createdAtUtc: now,
            publishedAtUtc: null);

        var unsafeErrorCode = ResolveUnsafeErrorCode(request, now);
        if (unsafeErrorCode is not null)
        {
            var rejected = pending with
            {
                Status = ArcSignalPublicationStatus.RejectedUnsafe,
                ErrorCode = unsafeErrorCode
            };
            await store.UpsertAsync(rejected, cancellationToken).ConfigureAwait(false);
            return new ArcSignalPublicationResult(rejected, AlreadyExisted: false);
        }

        await store.UpsertAsync(pending, cancellationToken).ConfigureAwait(false);

        var currentOptions = options.CurrentValue;
        if (!currentOptions.Enabled)
        {
            var skipped = pending with
            {
                Status = ArcSignalPublicationStatus.SkippedDisabled,
                ErrorCode = "ARC_DISABLED"
            };
            await store.UpsertAsync(skipped, cancellationToken).ConfigureAwait(false);
            return new ArcSignalPublicationResult(skipped, AlreadyExisted: false);
        }

        var validation = optionsValidator.Validate(currentOptions, ArcSettlementOptionsValidationMode.Write);
        if (!validation.IsValid)
        {
            var failed = pending with
            {
                Status = ArcSignalPublicationStatus.Failed,
                ErrorCode = "ARC_CONFIG_INVALID"
            };
            await store.UpsertAsync(failed, cancellationToken).ConfigureAwait(false);
            return new ArcSignalPublicationResult(failed, AlreadyExisted: false);
        }

        try
        {
            var published = await publisher.PublishAsync(
                    new ArcSignalRegistryPublishPayload(
                        signalId,
                        request.SignalProof.AgentId,
                        request.SignalProof.Venue,
                        request.SignalProof.StrategyId,
                        NormalizeBytes32Hash(request.SignalProof.ReasoningHash),
                        NormalizeBytes32Hash(request.SignalProof.RiskEnvelopeHash),
                        request.SignalProof.ExpectedEdgeBps,
                        request.SignalProof.MaxNotionalUsdc,
                        request.SignalProof.ValidUntilUtc),
                    cancellationToken)
                .ConfigureAwait(false);

            var completed = pending with
            {
                Status = published.Confirmed
                    ? ArcSignalPublicationStatus.Confirmed
                    : ArcSignalPublicationStatus.Submitted,
                TransactionHash = published.TransactionHash,
                ExplorerUrl = BuildExplorerUrl(currentOptions.BlockExplorerBaseUrl, published.TransactionHash),
                PublishedAtUtc = now
            };
            await store.UpsertAsync(completed, cancellationToken).ConfigureAwait(false);
            return new ArcSignalPublicationResult(completed, AlreadyExisted: false);
        }
        catch (ArcSignalRegistryDuplicateException)
        {
            var duplicate = pending with
            {
                Status = ArcSignalPublicationStatus.Duplicate,
                ErrorCode = "ARC_SIGNAL_DUPLICATE"
            };
            await store.UpsertAsync(duplicate, cancellationToken).ConfigureAwait(false);
            return new ArcSignalPublicationResult(duplicate, AlreadyExisted: false);
        }
        catch (Exception)
        {
            var failed = pending with
            {
                Status = ArcSignalPublicationStatus.Failed,
                ErrorCode = "ARC_PUBLISH_FAILED"
            };
            await store.UpsertAsync(failed, cancellationToken).ConfigureAwait(false);
            return new ArcSignalPublicationResult(failed, AlreadyExisted: false);
        }
    }

    public Task<IReadOnlyList<ArcSignalPublicationRecord>> ListAsync(
        ArcSignalPublicationQuery query,
        CancellationToken cancellationToken = default)
    {
        var limit = query.Limit <= 0 ? 20 : Math.Min(query.Limit, 200);
        return store.ListAsync(limit, cancellationToken);
    }

    public Task<ArcSignalPublicationRecord?> GetAsync(
        string signalId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(signalId);
        return store.GetBySignalIdAsync(NormalizeBytes32Hash(signalId), cancellationToken);
    }

    private static ArcSignalPublicationRecord CreateRecord(
        PublishArcSignalRequest request,
        string signalId,
        string signalHash,
        ArcSignalPublicationStatus status,
        string? transactionHash,
        string? explorerUrl,
        string? errorCode,
        DateTimeOffset createdAtUtc,
        DateTimeOffset? publishedAtUtc)
        => new(
            signalId,
            request.SignalProof.SourceKind,
            request.SignalProof.SourceId,
            request.SignalProof.AgentId,
            request.SignalProof.StrategyId,
            request.SignalProof.MarketId,
            request.SignalProof.Venue,
            request.SignalProof.ReasoningHash,
            request.SignalProof.RiskEnvelopeHash,
            request.SignalProof.ExpectedEdgeBps,
            request.SignalProof.MaxNotionalUsdc,
            request.SignalProof.ValidUntilUtc,
            status,
            signalHash,
            request.SourcePolicyHash,
            transactionHash,
            explorerUrl,
            errorCode,
            createdAtUtc,
            publishedAtUtc,
            request.Actor,
            request.Reason);

    private static string? ResolveUnsafeErrorCode(PublishArcSignalRequest request, DateTimeOffset now)
    {
        if (request.SignalProof.ValidUntilUtc <= now)
        {
            return "SOURCE_EXPIRED";
        }

        if (request.SourceReviewStatus is ArcSignalSourceReviewStatus.Rejected or ArcSignalSourceReviewStatus.Expired)
        {
            return "SOURCE_NOT_TRADE_READY";
        }

        if (request.SignalProof.SourceKind == ArcProofSourceKind.Opportunity
            && request.SourceReviewStatus is not (ArcSignalSourceReviewStatus.Approved or ArcSignalSourceReviewStatus.Published))
        {
            return "OPPORTUNITY_NOT_APPROVED";
        }

        if (request.SignalProof.SourceKind == ArcProofSourceKind.StrategyDecision)
        {
            if (string.IsNullOrWhiteSpace(request.SignalProof.MarketId)
                || string.IsNullOrWhiteSpace(request.SignalProof.StrategyId)
                || string.IsNullOrWhiteSpace(request.SignalProof.ReasoningHash)
                || string.IsNullOrWhiteSpace(request.SignalProof.RiskEnvelopeHash))
            {
                return "DECISION_CONTEXT_INCOMPLETE";
            }
        }

        if (!IsBytes32Hash(request.SignalProof.ReasoningHash) || !IsBytes32Hash(request.SignalProof.RiskEnvelopeHash))
        {
            return "INVALID_PROOF_HASH";
        }

        if (string.IsNullOrWhiteSpace(request.Actor) || string.IsNullOrWhiteSpace(request.Reason))
        {
            return "MISSING_ACTOR_OR_REASON";
        }

        return null;
    }

    private static string? BuildExplorerUrl(string baseUrl, string transactionHash)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return null;
        }

        return $"{baseUrl.TrimEnd('/')}/tx/{transactionHash}";
    }

    private static string NormalizeBytes32Hash(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? value.ToLowerInvariant()
            : $"0x{value.ToLowerInvariant()}";
        if (!IsBytes32Hash(normalized))
        {
            throw new ArgumentException("Value must be a 32-byte hex hash.", nameof(value));
        }

        return normalized;
    }

    private static bool IsBytes32Hash(string value)
    {
        var normalized = value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? value[2..]
            : value;
        return normalized.Length == 64 && normalized.All(Uri.IsHexDigit);
    }
}
