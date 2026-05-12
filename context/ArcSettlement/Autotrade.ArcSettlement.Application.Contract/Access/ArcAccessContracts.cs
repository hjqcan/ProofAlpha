using System.Text.Json.Serialization;
using Autotrade.Application.Services;

namespace Autotrade.ArcSettlement.Application.Contract.Access;

[JsonConverter(typeof(JsonStringEnumConverter<ArcEntitlementPermission>))]
public enum ArcEntitlementPermission
{
    ViewSignals = 0,
    ViewReasoning = 1,
    ExportSignal = 2,
    RequestPaperAutoTrade = 3,
    RequestLiveAutoTrade = 4,
    PublishSignal = 5,
    RecordSettlement = 6
}

[JsonConverter(typeof(JsonStringEnumConverter<ArcStrategyAccessStatusCode>))]
public enum ArcStrategyAccessStatusCode
{
    Disabled = 0,
    Active = 1,
    Expired = 2,
    MissingWallet = 3,
    InvalidWallet = 4,
    MissingStrategy = 5,
    NotFound = 6
}

public sealed record ArcSubscriptionPlan(
    int PlanId,
    string StrategyKey,
    string PlanName,
    string Tier,
    decimal PriceUsdc,
    long DurationSeconds,
    IReadOnlyList<ArcEntitlementPermission> Permissions,
    int? MaxMarkets,
    bool AutoTradingAllowed,
    bool LiveTradingAllowed,
    DateTimeOffset CreatedAtUtc);

public sealed record ArcSubscriptionMirror(
    string WalletAddress,
    string StrategyKey,
    string Tier,
    IReadOnlyList<ArcEntitlementPermission> Permissions,
    DateTimeOffset ExpiresAtUtc,
    string SourceTransactionHash,
    DateTimeOffset SyncedAtUtc,
    long? SourceBlockNumber = null,
    int? PlanId = null);

public sealed record ArcEntitlement(
    string WalletAddress,
    string StrategyKey,
    string Tier,
    IReadOnlyList<ArcEntitlementPermission> Permissions,
    DateTimeOffset ExpiresAtUtc,
    string SourceTransactionHash);

public sealed record ArcAccessSyncCursor(
    string ContractAddress,
    string StrategyKey,
    long LastSyncedBlock,
    DateTimeOffset UpdatedAtUtc);

public sealed record SyncArcAccessRequest(
    string WalletAddress,
    string StrategyKey,
    int PlanId,
    string SourceTransactionHash,
    DateTimeOffset ExpiresAtUtc,
    long? SourceBlockNumber = null);

public sealed record ArcSubscriptionSyncResult(
    ArcSubscriptionMirror Mirror,
    bool IsExpired,
    DateTimeOffset SyncedAtUtc);

public sealed record ArcStrategyAccessStatus(
    string WalletAddress,
    string StrategyKey,
    ArcStrategyAccessStatusCode StatusCode,
    bool HasAccess,
    string Reason,
    IReadOnlyList<ArcEntitlementPermission> Permissions,
    DateTimeOffset CheckedAtUtc,
    string? Tier = null,
    DateTimeOffset? ExpiresAtUtc = null,
    string? SourceTransactionHash = null,
    DateTimeOffset? SyncedAtUtc = null)
{
    public bool CanViewSignals => HasAccess && Permissions.Contains(ArcEntitlementPermission.ViewSignals);
}

public interface IArcSubscriptionPlanService : IApplicationService
{
    IReadOnlyList<ArcSubscriptionPlan> ListPlans();
}

public interface IArcStrategyAccessReader : IApplicationService
{
    Task<ArcStrategyAccessStatus> GetAccessAsync(
        string walletAddress,
        string strategyKey,
        CancellationToken cancellationToken = default);
}

public sealed class ArcSubscriptionSyncException(
    string errorCode,
    string message) : InvalidOperationException(message)
{
    public string ErrorCode { get; } = errorCode;
}

public interface IArcSubscriptionSyncService : IApplicationService
{
    Task<ArcSubscriptionSyncResult> SyncAsync(
        SyncArcAccessRequest request,
        CancellationToken cancellationToken = default);
}
