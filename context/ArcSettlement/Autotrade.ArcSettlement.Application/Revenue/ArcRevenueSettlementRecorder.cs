using System.Security.Cryptography;
using System.Text;
using Autotrade.ArcSettlement.Application.Configuration;
using Autotrade.ArcSettlement.Application.Contract.Configuration;
using Autotrade.ArcSettlement.Application.Contract.Proofs;
using Autotrade.ArcSettlement.Application.Contract.Revenue;
using Microsoft.Extensions.Options;

namespace Autotrade.ArcSettlement.Application.Revenue;

public interface IArcRevenueSettlementStore
{
    Task<ArcRevenueSettlementRecord?> GetBySettlementIdAsync(
        string settlementId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ArcRevenueSettlementRecord>> ListAsync(
        int limit,
        CancellationToken cancellationToken = default);

    Task UpsertAsync(
        ArcRevenueSettlementRecord record,
        CancellationToken cancellationToken = default);
}

public interface IArcRevenueSettlementPublisher
{
    Task<ArcRevenueSettlementPublishResult> PublishAsync(
        ArcRevenueSettlementPublishPayload payload,
        CancellationToken cancellationToken = default);
}

public sealed record ArcRevenueSettlementPublishPayload(
    string SettlementId,
    string SignalId,
    string TokenAddress,
    string GrossAmountMicroUsdc,
    IReadOnlyList<string> Recipients,
    IReadOnlyList<int> ShareBps);

public sealed record ArcRevenueSettlementPublishResult(
    string TransactionHash,
    bool Confirmed);

public sealed class ArcRevenueSettlementDuplicateException(string message) : InvalidOperationException(message);

public sealed class ArcRevenueSettlementRecorder(
    IArcRevenueSettlementStore settlementStore,
    IArcRevenueSettlementPublisher publisher,
    ArcSettlementOptionsValidator optionsValidator,
    IOptionsMonitor<ArcSettlementOptions> options,
    TimeProvider timeProvider) : IArcRevenueSettlementRecorder
{
    private const decimal UsdcScale = 1_000_000m;
    private const int FullShareBps = 10_000;

    public async Task<ArcRevenueSettlementResult> RecordAsync(
        ArcRevenueSettlementRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var currentOptions = options.CurrentValue;
        var normalized = NormalizeRequest(request, currentOptions);
        var existing = await settlementStore.GetBySettlementIdAsync(normalized.SettlementId, cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            return new ArcRevenueSettlementResult(existing, AlreadyRecorded: true);
        }

        var now = timeProvider.GetUtcNow();
        var settlementHash = HashStable(new SettlementHashMaterial(
            normalized.SettlementId,
            normalized.SourceKind,
            normalized.SignalId,
            normalized.ExecutionId,
            normalized.WalletAddress,
            normalized.StrategyId,
            normalized.GrossUsdc,
            normalized.GrossMicroUsdc,
            normalized.TokenAddress,
            normalized.Shares,
            normalized.Reason,
            normalized.Simulated,
            normalized.SourceTransactionHash,
            normalized.CreatedAtUtc));
        var pending = new ArcRevenueSettlementRecord(
            normalized.SettlementId,
            normalized.SourceKind,
            normalized.SignalId,
            normalized.ExecutionId,
            normalized.WalletAddress,
            normalized.StrategyId,
            normalized.GrossUsdc,
            normalized.GrossMicroUsdc,
            normalized.TokenAddress,
            normalized.Shares,
            normalized.Reason,
            normalized.Simulated,
            normalized.SourceTransactionHash,
            settlementHash,
            TransactionHash: null,
            ExplorerUrl: null,
            ArcRevenueSettlementStatus.Pending,
            ErrorCode: null,
            normalized.CreatedAtUtc,
            now);

        await settlementStore.UpsertAsync(pending, cancellationToken).ConfigureAwait(false);

        if (!currentOptions.Enabled)
        {
            var skipped = pending with
            {
                Status = ArcRevenueSettlementStatus.SkippedDisabled,
                ErrorCode = "ARC_DISABLED"
            };
            await settlementStore.UpsertAsync(skipped, cancellationToken).ConfigureAwait(false);
            return new ArcRevenueSettlementResult(skipped, AlreadyRecorded: false);
        }

        var validation = optionsValidator.Validate(currentOptions, ArcSettlementOptionsValidationMode.Write);
        if (!validation.IsValid)
        {
            var failed = pending with
            {
                Status = ArcRevenueSettlementStatus.Failed,
                ErrorCode = "ARC_CONFIG_INVALID"
            };
            await settlementStore.UpsertAsync(failed, cancellationToken).ConfigureAwait(false);
            return new ArcRevenueSettlementResult(failed, AlreadyRecorded: false);
        }

        try
        {
            var published = await publisher.PublishAsync(
                    new ArcRevenueSettlementPublishPayload(
                        pending.SettlementId,
                        pending.SignalId,
                        pending.TokenAddress,
                        pending.GrossMicroUsdc.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        pending.Shares.Select(share => share.WalletAddress).ToArray(),
                        pending.Shares.Select(share => share.ShareBps).ToArray()),
                    cancellationToken)
                .ConfigureAwait(false);
            var completed = pending with
            {
                Status = published.Confirmed
                    ? ArcRevenueSettlementStatus.Confirmed
                    : ArcRevenueSettlementStatus.Submitted,
                TransactionHash = published.TransactionHash,
                ExplorerUrl = BuildExplorerUrl(currentOptions.BlockExplorerBaseUrl, published.TransactionHash),
                RecordedAtUtc = now
            };
            await settlementStore.UpsertAsync(completed, cancellationToken).ConfigureAwait(false);
            return new ArcRevenueSettlementResult(completed, AlreadyRecorded: false);
        }
        catch (ArcRevenueSettlementDuplicateException)
        {
            var duplicate = pending with
            {
                Status = ArcRevenueSettlementStatus.Failed,
                ErrorCode = "SETTLEMENT_ALREADY_RECORDED"
            };
            await settlementStore.UpsertAsync(duplicate, cancellationToken).ConfigureAwait(false);
            return new ArcRevenueSettlementResult(duplicate, AlreadyRecorded: true);
        }
        catch (Exception)
        {
            var failed = pending with
            {
                Status = ArcRevenueSettlementStatus.Failed,
                ErrorCode = "REVENUE_SETTLEMENT_PUBLISH_FAILED"
            };
            await settlementStore.UpsertAsync(failed, cancellationToken).ConfigureAwait(false);
            return new ArcRevenueSettlementResult(failed, AlreadyRecorded: false);
        }
    }

    public Task<IReadOnlyList<ArcRevenueSettlementRecord>> ListAsync(
        int limit,
        CancellationToken cancellationToken = default)
        => settlementStore.ListAsync(limit, cancellationToken);

    public Task<ArcRevenueSettlementRecord?> GetAsync(
        string settlementId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settlementId);
        return settlementStore.GetBySettlementIdAsync(NormalizeBytes32Hash(settlementId), cancellationToken);
    }

    private NormalizedSettlementRequest NormalizeRequest(
        ArcRevenueSettlementRequest request,
        ArcSettlementOptions currentOptions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SignalId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.WalletAddress);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.StrategyId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Reason);

        if (request.GrossUsdc <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "GrossUsdc must be greater than zero.");
        }

        if (request.SourceKind == ArcRevenueSourceKind.ManualDemoSettlement && !request.Simulated)
        {
            throw new ArgumentException("Manual demo revenue settlements must be marked simulated.", nameof(request));
        }

        var signalId = NormalizeBytes32Hash(request.SignalId);
        var walletAddress = NormalizeAddress(request.WalletAddress, nameof(request.WalletAddress));
        var tokenAddress = NormalizeAddress(
            string.IsNullOrWhiteSpace(request.TokenAddress)
                ? currentOptions.Revenue.TokenAddress
                : request.TokenAddress,
            nameof(request.TokenAddress));
        var grossMicroUsdc = ToMicroUsdc(request.GrossUsdc);
        var shares = ResolveShares(request.Shares, currentOptions, grossMicroUsdc);
        var sourceTransactionHash = string.IsNullOrWhiteSpace(request.SourceTransactionHash)
            ? null
            : NormalizeBytes32Hash(request.SourceTransactionHash);
        var executionId = string.IsNullOrWhiteSpace(request.ExecutionId)
            ? null
            : request.ExecutionId.Trim();
        var reason = request.Reason.Trim();
        var strategyId = request.StrategyId.Trim();
        var settlementId = string.IsNullOrWhiteSpace(request.SettlementId)
            ? HashStable(new SettlementIdMaterial(
                request.SourceKind,
                signalId,
                executionId,
                walletAddress,
                strategyId,
                request.GrossUsdc,
                grossMicroUsdc,
                tokenAddress,
                shares.Select(share => new ShareIdMaterial(
                    share.RecipientKind,
                    share.WalletAddress,
                    share.ShareBps)).ToArray(),
                reason,
                request.Simulated,
                sourceTransactionHash))
            : NormalizeBytes32Hash(request.SettlementId);

        return new NormalizedSettlementRequest(
            settlementId,
            request.SourceKind,
            signalId,
            executionId,
            walletAddress,
            strategyId,
            request.GrossUsdc,
            grossMicroUsdc,
            tokenAddress,
            shares,
            reason,
            request.Simulated,
            sourceTransactionHash,
            request.CreatedAtUtc ?? timeProvider.GetUtcNow());
    }

    private static IReadOnlyList<ArcRevenueSplitAllocation> ResolveShares(
        IReadOnlyList<ArcRevenueSplitShareRequest>? requestedShares,
        ArcSettlementOptions currentOptions,
        long grossMicroUsdc)
    {
        var shares = requestedShares is { Count: > 0 }
            ? requestedShares
            : ResolveConfiguredOrDefaultShares(currentOptions)
                .Select(share => new ArcRevenueSplitShareRequest(
                    share.RecipientKind,
                    share.WalletAddress,
                    share.ShareBps))
                .ToArray();
        if (shares.Count == 0)
        {
            throw new ArgumentException("At least one revenue split share is required.", nameof(requestedShares));
        }

        var totalBps = 0;
        var normalizedShares = new List<NormalizedShare>(shares.Count);
        for (var index = 0; index < shares.Count; index++)
        {
            var share = shares[index];
            if (share.ShareBps <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(requestedShares), "Revenue split share bps must be greater than zero.");
            }

            totalBps += share.ShareBps;
            normalizedShares.Add(new NormalizedShare(
                index,
                share.RecipientKind,
                NormalizeAddress(share.WalletAddress, nameof(share.WalletAddress)),
                share.ShareBps));
        }

        if (totalBps != FullShareBps)
        {
            throw new ArgumentException("Revenue split share bps must sum to 10000.", nameof(requestedShares));
        }

        var rawAllocations = normalizedShares
            .Select(share =>
            {
                var numerator = (decimal)grossMicroUsdc * share.ShareBps;
                var baseAmount = (long)Math.Floor(numerator / FullShareBps);
                var remainder = numerator - (baseAmount * FullShareBps);
                return new RawAllocation(share, baseAmount, remainder);
            })
            .ToArray();
        var allocated = rawAllocations.Sum(item => item.AmountMicroUsdc);
        var remaining = grossMicroUsdc - allocated;
        var bonuses = rawAllocations.ToDictionary(
            item => item.Share.Index,
            _ => 0L);
        foreach (var item in rawAllocations
                     .OrderByDescending(item => item.Remainder)
                     .ThenByDescending(item => item.Share.ShareBps)
                     .ThenBy(item => item.Share.RecipientKind)
                     .ThenBy(item => item.Share.WalletAddress, StringComparer.Ordinal)
                     .Take((int)remaining))
        {
            bonuses[item.Share.Index]++;
        }

        return rawAllocations
            .OrderBy(item => item.Share.Index)
            .Select(item =>
            {
                var amountMicroUsdc = item.AmountMicroUsdc + bonuses[item.Share.Index];
                return new ArcRevenueSplitAllocation(
                    item.Share.RecipientKind,
                    item.Share.WalletAddress,
                    item.Share.ShareBps,
                    amountMicroUsdc,
                    FromMicroUsdc(amountMicroUsdc));
            })
            .ToArray();
    }

    private static IReadOnlyList<ArcRevenueSplitRecipientOptions> ResolveConfiguredOrDefaultShares(
        ArcSettlementOptions currentOptions)
        => currentOptions.Revenue.Shares.Count > 0
            ? currentOptions.Revenue.Shares
            :
            [
                new ArcRevenueSplitRecipientOptions
                {
                    RecipientKind = ArcRevenueRecipientKind.AgentOwner,
                    WalletAddress = "0x1000000000000000000000000000000000000001",
                    ShareBps = 7000
                },
                new ArcRevenueSplitRecipientOptions
                {
                    RecipientKind = ArcRevenueRecipientKind.StrategyAuthor,
                    WalletAddress = "0x2000000000000000000000000000000000000002",
                    ShareBps = 2000
                },
                new ArcRevenueSplitRecipientOptions
                {
                    RecipientKind = ArcRevenueRecipientKind.Platform,
                    WalletAddress = "0x3000000000000000000000000000000000000003",
                    ShareBps = 1000
                }
            ];

    private static long ToMicroUsdc(decimal grossUsdc)
    {
        var scaled = grossUsdc * UsdcScale;
        if (decimal.Truncate(scaled) != scaled)
        {
            throw new ArgumentException("GrossUsdc supports at most 6 decimal places.", nameof(grossUsdc));
        }

        if (scaled > long.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(grossUsdc), "GrossUsdc is too large.");
        }

        return decimal.ToInt64(scaled);
    }

    private static decimal FromMicroUsdc(long amountMicroUsdc)
        => amountMicroUsdc / UsdcScale;

    private static string NormalizeAddress(string? value, string fieldName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, fieldName);
        var normalized = value.Trim().ToLowerInvariant();
        if (normalized.Length != 42 || !normalized.StartsWith("0x", StringComparison.Ordinal) || !normalized[2..].All(Uri.IsHexDigit))
        {
            throw new ArgumentException($"{fieldName} must be an EVM address.", fieldName);
        }

        if (normalized[2..].All(character => character == '0'))
        {
            throw new ArgumentException($"{fieldName} cannot be the zero address.", fieldName);
        }

        return normalized;
    }

    private static string NormalizeBytes32Hash(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? value.ToLowerInvariant()
            : $"0x{value.ToLowerInvariant()}";
        if (normalized.Length != 66 || !normalized[2..].All(Uri.IsHexDigit))
        {
            throw new ArgumentException("Value must be a 32-byte hex hash.", nameof(value));
        }

        return normalized;
    }

    private static string? BuildExplorerUrl(string baseUrl, string transactionHash)
        => string.IsNullOrWhiteSpace(baseUrl)
            ? null
            : $"{baseUrl.TrimEnd('/')}/tx/{transactionHash}";

    private static string HashStable<T>(T value)
    {
        var json = ArcProofJson.SerializeStable(value);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return $"0x{Convert.ToHexString(bytes).ToLowerInvariant()}";
    }

    private sealed record NormalizedShare(
        int Index,
        ArcRevenueRecipientKind RecipientKind,
        string WalletAddress,
        int ShareBps);

    private sealed record RawAllocation(
        NormalizedShare Share,
        long AmountMicroUsdc,
        decimal Remainder);

    private sealed record NormalizedSettlementRequest(
        string SettlementId,
        ArcRevenueSourceKind SourceKind,
        string SignalId,
        string? ExecutionId,
        string WalletAddress,
        string StrategyId,
        decimal GrossUsdc,
        long GrossMicroUsdc,
        string TokenAddress,
        IReadOnlyList<ArcRevenueSplitAllocation> Shares,
        string Reason,
        bool Simulated,
        string? SourceTransactionHash,
        DateTimeOffset CreatedAtUtc);

    private sealed record SettlementIdMaterial(
        ArcRevenueSourceKind SourceKind,
        string SignalId,
        string? ExecutionId,
        string WalletAddress,
        string StrategyId,
        decimal GrossUsdc,
        long GrossMicroUsdc,
        string TokenAddress,
        IReadOnlyList<ShareIdMaterial> Shares,
        string Reason,
        bool Simulated,
        string? SourceTransactionHash);

    private sealed record ShareIdMaterial(
        ArcRevenueRecipientKind RecipientKind,
        string WalletAddress,
        int ShareBps);

    private sealed record SettlementHashMaterial(
        string SettlementId,
        ArcRevenueSourceKind SourceKind,
        string SignalId,
        string? ExecutionId,
        string WalletAddress,
        string StrategyId,
        decimal GrossUsdc,
        long GrossMicroUsdc,
        string TokenAddress,
        IReadOnlyList<ArcRevenueSplitAllocation> Shares,
        string Reason,
        bool Simulated,
        string? SourceTransactionHash,
        DateTimeOffset CreatedAtUtc);
}
