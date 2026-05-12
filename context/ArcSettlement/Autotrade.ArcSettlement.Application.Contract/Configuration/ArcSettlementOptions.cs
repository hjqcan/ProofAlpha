using Autotrade.ArcSettlement.Domain.Shared;

namespace Autotrade.ArcSettlement.Application.Contract.Configuration;

public sealed record ArcSettlementOptions
{
    public const string SectionName = "ArcSettlement";

    public bool Enabled { get; init; }

    public long ChainId { get; init; }

    public string RpcUrl { get; init; } = string.Empty;

    public string BlockExplorerBaseUrl { get; init; } = string.Empty;

    public string SignalPublicationStorePath { get; init; } = "artifacts/arc-settlement/signals.json";

    public string EntitlementMirrorStorePath { get; init; } = "artifacts/arc-settlement/entitlements.json";

    public string PerformanceOutcomeStorePath { get; init; } = "artifacts/arc-settlement/performance-outcomes.json";

    public string ProvenanceStorePath { get; init; } = "artifacts/arc-settlement/provenance.json";

    public ArcSettlementContractsOptions Contracts { get; init; } = new();

    public ArcSettlementWalletOptions Wallet { get; init; } = new();

    public ArcSettlementEvmPublisherOptions EvmPublisher { get; init; } = new();

    public ArcSettlementSignalProofOptions SignalProof { get; init; } = new();

    public IReadOnlyList<ArcSettlementSubscriptionPlanOptions> SubscriptionPlans { get; init; } = [];
}

public sealed record ArcSettlementContractsOptions
{
    public string SignalRegistry { get; init; } = string.Empty;

    public string StrategyAccess { get; init; } = string.Empty;

    public string PerformanceLedger { get; init; } = string.Empty;

    public string RevenueSettlement { get; init; } = string.Empty;
}

public sealed record ArcSettlementWalletOptions
{
    public string PrivateKeyEnvironmentVariable { get; init; } =
        ArcSettlementConstants.DefaultPrivateKeyEnvironmentVariable;
}

public sealed record ArcSettlementEvmPublisherOptions
{
    public string Tool { get; init; } = "Hardhat";

    public string ContractsWorkspacePath { get; init; } = "interfaces/ArcContracts";

    public string NetworkName { get; init; } = "localhost";

    public int RequestTimeoutSeconds { get; init; } = 120;
}

public sealed record ArcSettlementSignalProofOptions
{
    public string AgentAddress { get; init; } = string.Empty;

    public string Venue { get; init; } = "polymarket";

    public string OpportunityStrategyId { get; init; } = "llm_opportunity";

    public int DecisionValidForMinutes { get; init; } = 30;

    public string DefaultRiskTier { get; init; } = "paper";
}

public sealed record ArcSettlementSubscriptionPlanOptions
{
    public int PlanId { get; init; }

    public string StrategyKey { get; init; } = string.Empty;

    public string PlanName { get; init; } = string.Empty;

    public string Tier { get; init; } = string.Empty;

    public decimal PriceUsdc { get; init; }

    public int DurationDays { get; init; }

    public long DurationSeconds { get; init; }

    public IReadOnlyList<string> Permissions { get; init; } = [];

    public int? MaxMarkets { get; init; }

    public bool AutoTradingAllowed { get; init; }

    public bool LiveTradingAllowed { get; init; }

    public DateTimeOffset? CreatedAtUtc { get; init; }
}
