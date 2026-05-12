using Autotrade.ArcSettlement.Application.Contract.Access;
using Autotrade.ArcSettlement.Application.Contract.Configuration;
using Microsoft.Extensions.Options;

namespace Autotrade.ArcSettlement.Application.Access;

public interface IArcEntitlementMirrorStore
{
    Task<ArcSubscriptionMirror?> GetAsync(
        string walletAddress,
        string strategyKey,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ArcSubscriptionMirror>> ListAsync(
        int limit,
        CancellationToken cancellationToken = default);

    Task UpsertAsync(
        ArcSubscriptionMirror mirror,
        CancellationToken cancellationToken = default);
}

public sealed class ArcSubscriptionPlanService(
    IOptionsMonitor<ArcSettlementOptions> options,
    TimeProvider timeProvider) : IArcSubscriptionPlanService
{
    public IReadOnlyList<ArcSubscriptionPlan> ListPlans()
        => options.CurrentValue.SubscriptionPlans
            .Select(ToPlan)
            .OrderBy(plan => plan.PlanId)
            .ToArray();

    private ArcSubscriptionPlan ToPlan(ArcSettlementSubscriptionPlanOptions plan)
    {
        var durationSeconds = plan.DurationSeconds > 0
            ? plan.DurationSeconds
            : checked((long)plan.DurationDays * 24 * 60 * 60);

        return new ArcSubscriptionPlan(
            plan.PlanId,
            plan.StrategyKey.Trim(),
            string.IsNullOrWhiteSpace(plan.PlanName) ? plan.Tier.Trim() : plan.PlanName.Trim(),
            plan.Tier.Trim(),
            plan.PriceUsdc,
            durationSeconds,
            plan.Permissions.Select(ParsePermission).ToArray(),
            plan.MaxMarkets,
            plan.AutoTradingAllowed,
            plan.LiveTradingAllowed,
            plan.CreatedAtUtc ?? timeProvider.GetUtcNow());
    }

    private static ArcEntitlementPermission ParsePermission(string value)
        => Enum.TryParse<ArcEntitlementPermission>(value, ignoreCase: true, out var permission)
            ? permission
            : throw new InvalidOperationException($"Unknown Arc entitlement permission '{value}'.");
}

public sealed class ArcStrategyAccessReader(
    IArcEntitlementMirrorStore mirrorStore,
    IOptionsMonitor<ArcSettlementOptions> options,
    TimeProvider timeProvider) : IArcStrategyAccessReader
{
    public async Task<ArcStrategyAccessStatus> GetAccessAsync(
        string walletAddress,
        string strategyKey,
        CancellationToken cancellationToken = default)
    {
        var checkedAt = timeProvider.GetUtcNow();
        var normalizedStrategyKey = strategyKey?.Trim() ?? string.Empty;

        if (!options.CurrentValue.Enabled)
        {
            return Denied(
                walletAddress,
                normalizedStrategyKey,
                ArcStrategyAccessStatusCode.Disabled,
                "ARC_SETTLEMENT_DISABLED",
                checkedAt);
        }

        if (string.IsNullOrWhiteSpace(walletAddress))
        {
            return Denied(
                string.Empty,
                normalizedStrategyKey,
                ArcStrategyAccessStatusCode.MissingWallet,
                "MISSING_WALLET",
                checkedAt);
        }

        if (!TryNormalizeWalletAddress(walletAddress, out var normalizedWalletAddress))
        {
            return Denied(
                walletAddress.Trim(),
                normalizedStrategyKey,
                ArcStrategyAccessStatusCode.InvalidWallet,
                "INVALID_WALLET",
                checkedAt);
        }

        if (string.IsNullOrWhiteSpace(normalizedStrategyKey))
        {
            return Denied(
                normalizedWalletAddress,
                normalizedStrategyKey,
                ArcStrategyAccessStatusCode.MissingStrategy,
                "MISSING_STRATEGY",
                checkedAt);
        }

        var mirror = await mirrorStore
            .GetAsync(normalizedWalletAddress, normalizedStrategyKey, cancellationToken)
            .ConfigureAwait(false);

        if (mirror is null)
        {
            return Denied(
                normalizedWalletAddress,
                normalizedStrategyKey,
                ArcStrategyAccessStatusCode.NotFound,
                "ACCESS_NOT_FOUND",
                checkedAt);
        }

        if (mirror.ExpiresAtUtc <= checkedAt)
        {
            return new ArcStrategyAccessStatus(
                normalizedWalletAddress,
                normalizedStrategyKey,
                ArcStrategyAccessStatusCode.Expired,
                HasAccess: false,
                "ACCESS_EXPIRED",
                mirror.Permissions,
                checkedAt,
                mirror.Tier,
                mirror.ExpiresAtUtc,
                mirror.SourceTransactionHash,
                mirror.SyncedAtUtc);
        }

        return new ArcStrategyAccessStatus(
            normalizedWalletAddress,
            normalizedStrategyKey,
            ArcStrategyAccessStatusCode.Active,
            HasAccess: true,
            "ACCESS_ACTIVE",
            mirror.Permissions,
            checkedAt,
            mirror.Tier,
            mirror.ExpiresAtUtc,
            mirror.SourceTransactionHash,
            mirror.SyncedAtUtc);
    }

    internal static bool TryNormalizeWalletAddress(string value, out string normalized)
    {
        normalized = string.Empty;
        var trimmed = value.Trim();
        if (trimmed.Length != 42 || !trimmed.StartsWith("0x", StringComparison.Ordinal))
        {
            return false;
        }

        var body = trimmed[2..];
        if (!body.All(Uri.IsHexDigit) || body.All(character => character == '0'))
        {
            return false;
        }

        normalized = $"0x{body.ToLowerInvariant()}";
        return true;
    }

    private static ArcStrategyAccessStatus Denied(
        string walletAddress,
        string strategyKey,
        ArcStrategyAccessStatusCode statusCode,
        string reason,
        DateTimeOffset checkedAt)
        => new(
            walletAddress,
            strategyKey,
            statusCode,
            HasAccess: false,
            reason,
            [],
            checkedAt);
}
