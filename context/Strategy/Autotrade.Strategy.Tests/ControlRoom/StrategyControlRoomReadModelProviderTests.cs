using Autotrade.Strategy.Application.ControlRoom;
using Autotrade.Strategy.Application.Contract.ControlRoom;
using Autotrade.Strategy.Application.Engine;
using Autotrade.Strategy.Application.Strategies.RepricingLag;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Autotrade.Strategy.Tests.ControlRoom;

public sealed class StrategyControlRoomReadModelProviderTests
{
    [Fact]
    public async Task FallbackReadModelIncludesEveryConfiguredStrategy()
    {
        var services = new ServiceCollection();
        services.AddStrategyControlRoomReadModel(BuildConfiguration());

        using var provider = services.BuildServiceProvider();
        var readModelProvider = provider.GetRequiredService<IStrategyControlRoomReadModelProvider>();

        var readModel = await readModelProvider.GetReadModelAsync();

        Assert.Equal(
            [
                "dual_leg_arbitrage",
                "endgame_sweep",
                "liquidity_maker",
                "liquidity_pulse",
                "llm_opportunity",
                "micro_volatility_scalper",
                "repricing_lag_arbitrage"
            ],
            readModel.Strategies.Select(strategy => strategy.StrategyId).Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void AddStrategyControlRoomReadModelDoesNotDuplicateOptionConfiguration()
    {
        var configuration = BuildConfiguration();
        var services = new ServiceCollection();

        services.AddStrategyControlRoomReadModel(configuration);
        services.AddStrategyControlRoomReadModel(configuration);

        Assert.Single(
            services,
            descriptor => descriptor.ServiceType == typeof(IConfigureOptions<RepricingLagArbitrageOptions>));
        Assert.Single(
            services,
            descriptor => descriptor.ServiceType == typeof(IOptionsChangeTokenSource<RepricingLagArbitrageOptions>));
    }

    private static IConfiguration BuildConfiguration()
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{StrategyEngineOptions.SectionName}:Enabled"] = "true",
                [$"{RepricingLagArbitrageOptions.SectionName}:AllowedSpotSources:0"] = "rtds:crypto_prices"
            })
            .Build();
    }
}
