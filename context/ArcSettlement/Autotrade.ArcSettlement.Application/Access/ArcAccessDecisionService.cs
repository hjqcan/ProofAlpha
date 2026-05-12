using Autotrade.ArcSettlement.Application.Contract.Access;

namespace Autotrade.ArcSettlement.Application.Access;

public sealed class ArcAccessDecisionService(
    IArcStrategyAccessReader accessReader) : IArcAccessDecisionService
{
    public async Task<ArcAccessDecision> EvaluateAsync(
        ArcAccessDecisionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var strategyKey = request.StrategyKey?.Trim() ?? string.Empty;
        var resourceKind = request.ResourceKind?.Trim() ?? string.Empty;
        var resourceId = request.ResourceId?.Trim() ?? string.Empty;

        var status = await accessReader
            .GetAccessAsync(request.WalletAddress ?? string.Empty, strategyKey, cancellationToken)
            .ConfigureAwait(false);

        if (!status.HasAccess)
        {
            return Denied(
                status,
                request.RequiredPermission,
                resourceKind,
                resourceId,
                status.Reason,
                ToReason(status.StatusCode));
        }

        if (!status.Permissions.Contains(request.RequiredPermission))
        {
            return Denied(
                status,
                request.RequiredPermission,
                resourceKind,
                resourceId,
                "MISSING_PERMISSION",
                $"Wallet entitlement does not include {request.RequiredPermission}.");
        }

        return new ArcAccessDecision(
            Allowed: true,
            "ACCESS_ALLOWED",
            "Access allowed by active Arc subscription entitlement.",
            request.RequiredPermission,
            status.StrategyKey,
            status.WalletAddress,
            resourceKind,
            resourceId,
            status.Tier,
            status.ExpiresAtUtc,
            status.SourceTransactionHash);
    }

    private static ArcAccessDecision Denied(
        ArcStrategyAccessStatus status,
        ArcEntitlementPermission requiredPermission,
        string resourceKind,
        string resourceId,
        string reasonCode,
        string reason)
        => new(
            Allowed: false,
            reasonCode,
            reason,
            requiredPermission,
            status.StrategyKey,
            string.IsNullOrWhiteSpace(status.WalletAddress) ? null : status.WalletAddress,
            resourceKind,
            resourceId,
            status.Tier,
            status.ExpiresAtUtc,
            status.SourceTransactionHash);

    private static string ToReason(ArcStrategyAccessStatusCode statusCode)
        => statusCode switch
        {
            ArcStrategyAccessStatusCode.Disabled => "Arc settlement access control is disabled.",
            ArcStrategyAccessStatusCode.Expired => "Wallet subscription entitlement is expired.",
            ArcStrategyAccessStatusCode.MissingWallet => "A demo wallet address is required for this Arc resource.",
            ArcStrategyAccessStatusCode.InvalidWallet => "The supplied demo wallet address is not a valid EVM address.",
            ArcStrategyAccessStatusCode.MissingStrategy => "A strategy key is required for this Arc resource.",
            ArcStrategyAccessStatusCode.NotFound => "No active Arc subscription entitlement was found for this wallet and strategy.",
            _ => "Arc access was denied."
        };
}
