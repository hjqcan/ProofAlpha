using Autotrade.MarketData.Infra.Data.Context;
using Autotrade.OpportunityDiscovery.Infra.Data.Context;
using Autotrade.SelfImprove.Infra.Data.Context;
using Autotrade.Strategy.Infra.Data.Context;
using Autotrade.Trading.Infra.Data.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Autotrade.Hosting;

public static class AutotradeDatabaseHealthChecks
{
    public static IHealthChecksBuilder AddAutotradeDatabaseHealthChecks(this IHealthChecksBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder
            .AddCheck<AutotradeDatabaseHealthCheck<TradingContext>>("trading_db", tags: ["ready"])
            .AddCheck<AutotradeDatabaseHealthCheck<MarketDataContext>>("marketdata_db", tags: ["ready"])
            .AddCheck<AutotradeDatabaseHealthCheck<StrategyContext>>("strategy_db", tags: ["ready"])
            .AddCheck<AutotradeDatabaseHealthCheck<SelfImproveContext>>("selfimprove_db", tags: ["ready"])
            .AddCheck<AutotradeDatabaseHealthCheck<OpportunityDiscoveryContext>>(
                "opportunity_discovery_db",
                tags: ["ready"]);
    }
}

internal sealed class AutotradeDatabaseHealthCheck<TContext>(TContext dbContext) : IHealthCheck
    where TContext : DbContext
{
    private readonly TContext _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var canConnect = await _dbContext.Database
                .CanConnectAsync(cancellationToken)
                .ConfigureAwait(false);

            return canConnect
                ? HealthCheckResult.Healthy("Database connection is healthy.")
                : HealthCheckResult.Unhealthy("Database connection failed.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Database connection failed.", ex);
        }
    }
}
