using Autotrade.MarketData.Infra.Data.Context;
using Autotrade.OpportunityDiscovery.Infra.Data.Context;
using Autotrade.SelfImprove.Infra.Data.Context;
using Autotrade.Strategy.Infra.Data.Context;
using Autotrade.Trading.Infra.Data.Context;

namespace Autotrade.Hosting;

internal sealed record AutotradeDatabaseContextDefinition(
    string Name,
    Type ContextType,
    string SentinelTable);

internal static class AutotradeDatabaseContextCatalog
{
    public static readonly AutotradeDatabaseContextDefinition[] Definitions =
    [
        new("trading", typeof(TradingContext), "TradingAccounts"),
        new("marketdata", typeof(MarketDataContext), "Markets"),
        new("strategy", typeof(StrategyContext), "StrategyDecisionLogs"),
        new("selfimprove", typeof(SelfImproveContext), "ImprovementRuns"),
        new("opportunity_discovery", typeof(OpportunityDiscoveryContext), "MarketOpportunities")
    ];
}
