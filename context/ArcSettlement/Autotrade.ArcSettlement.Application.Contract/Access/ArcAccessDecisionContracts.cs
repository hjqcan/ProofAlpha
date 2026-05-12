using Autotrade.Application.Services;

namespace Autotrade.ArcSettlement.Application.Contract.Access;

public sealed record ArcAccessDecisionRequest(
    string? WalletAddress,
    string StrategyKey,
    ArcEntitlementPermission RequiredPermission,
    string ResourceKind,
    string ResourceId);

public sealed record ArcAccessDecision(
    bool Allowed,
    string ReasonCode,
    string Reason,
    ArcEntitlementPermission RequiredPermission,
    string StrategyKey,
    string? WalletAddress,
    string ResourceKind,
    string ResourceId,
    string? Tier = null,
    DateTimeOffset? ExpiresAtUtc = null,
    string? EvidenceTransactionHash = null);

public interface IArcAccessDecisionService : IApplicationService
{
    Task<ArcAccessDecision> EvaluateAsync(
        ArcAccessDecisionRequest request,
        CancellationToken cancellationToken = default);
}
