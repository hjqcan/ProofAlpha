using Autotrade.ArcSettlement.Application.Contract.Access;

namespace Autotrade.ArcSettlement.Application.Access;

public sealed class ArcSubscriptionSyncService(
    IArcEntitlementMirrorStore mirrorStore,
    IArcSubscriptionPlanService planService,
    TimeProvider timeProvider) : IArcSubscriptionSyncService
{
    public async Task<ArcSubscriptionSyncResult> SyncAsync(
        SyncArcAccessRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!ArcStrategyAccessReader.TryNormalizeWalletAddress(request.WalletAddress, out var walletAddress))
        {
            throw new ArcSubscriptionSyncException("INVALID_WALLET", "Wallet address must be a non-zero EVM address.");
        }

        var strategyKey = request.StrategyKey.Trim();
        if (string.IsNullOrWhiteSpace(strategyKey))
        {
            throw new ArcSubscriptionSyncException("MISSING_STRATEGY", "Strategy key is required.");
        }

        if (!IsLikelyTransactionHash(request.SourceTransactionHash))
        {
            throw new ArcSubscriptionSyncException("INVALID_TX_HASH", "Source transaction hash must be a 32-byte hex value.");
        }

        if (request.SourceBlockNumber < 0)
        {
            throw new ArcSubscriptionSyncException("INVALID_BLOCK_NUMBER", "Source block number cannot be negative.");
        }

        var plan = planService.ListPlans().FirstOrDefault(item => item.PlanId == request.PlanId)
            ?? throw new ArcSubscriptionSyncException("PLAN_NOT_FOUND", $"Subscription plan {request.PlanId} was not found.");

        if (!string.Equals(plan.StrategyKey, strategyKey, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArcSubscriptionSyncException(
                "PLAN_STRATEGY_MISMATCH",
                $"Subscription plan {request.PlanId} is for strategy '{plan.StrategyKey}', not '{strategyKey}'.");
        }

        var syncedAt = timeProvider.GetUtcNow();
        var mirror = new ArcSubscriptionMirror(
            walletAddress,
            strategyKey,
            plan.Tier,
            plan.Permissions,
            request.ExpiresAtUtc,
            request.SourceTransactionHash.ToLowerInvariant(),
            syncedAt,
            request.SourceBlockNumber,
            plan.PlanId);

        await mirrorStore.UpsertAsync(mirror, cancellationToken).ConfigureAwait(false);

        return new ArcSubscriptionSyncResult(
            mirror,
            IsExpired: mirror.ExpiresAtUtc <= syncedAt,
            syncedAt);
    }

    private static bool IsLikelyTransactionHash(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length != 66 || !value.StartsWith("0x", StringComparison.Ordinal))
        {
            return false;
        }

        var body = value[2..];
        return body.All(Uri.IsHexDigit) && !body.All(character => character == '0');
    }
}
