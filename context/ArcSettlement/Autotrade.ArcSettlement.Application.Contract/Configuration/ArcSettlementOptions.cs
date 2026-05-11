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

    public ArcSettlementContractsOptions Contracts { get; init; } = new();

    public ArcSettlementWalletOptions Wallet { get; init; } = new();

    public ArcSettlementEvmPublisherOptions EvmPublisher { get; init; } = new();
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
