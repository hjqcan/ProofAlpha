using Autotrade.Strategy.Application.Contract.ControlRoom;
using Autotrade.Strategy.Application.Engine;
using Autotrade.Strategy.Application.Strategies.DualLeg;
using Autotrade.Strategy.Application.Strategies.Endgame;
using Autotrade.Strategy.Application.Strategies.LiquidityMaking;
using Autotrade.Strategy.Application.Strategies.LiquidityPulse;
using Autotrade.Strategy.Application.Strategies.Opportunity;
using Autotrade.Strategy.Application.Strategies.RepricingLag;
using Autotrade.Strategy.Application.Strategies.Volatility;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Autotrade.Strategy.Application.ControlRoom;

public static class StrategyControlRoomReadModelServiceCollectionExtensions
{
    public static IServiceCollection AddStrategyControlRoomReadModel(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        if (!services.Any(descriptor => descriptor.ServiceType == typeof(StrategyControlRoomReadModelOptionsRegistration)))
        {
            services.Configure<StrategyEngineOptions>(
                configuration.GetSection(StrategyEngineOptions.SectionName));
            services.Configure<DualLegArbitrageOptions>(
                configuration.GetSection(DualLegArbitrageOptions.SectionName));
            services.Configure<EndgameSweepOptions>(
                configuration.GetSection(EndgameSweepOptions.SectionName));
            services.Configure<LiquidityPulseOptions>(
                configuration.GetSection(LiquidityPulseOptions.SectionName));
            services.Configure<LiquidityMakerOptions>(
                configuration.GetSection(LiquidityMakerOptions.SectionName));
            services.Configure<MicroVolatilityScalperOptions>(
                configuration.GetSection(MicroVolatilityScalperOptions.SectionName));
            services.Configure<RepricingLagArbitrageOptions>(
                configuration.GetSection(RepricingLagArbitrageOptions.SectionName));
            services.Configure<LlmOpportunityOptions>(
                configuration.GetSection(LlmOpportunityOptions.SectionName));
            services.AddSingleton<StrategyControlRoomReadModelOptionsRegistration>();
        }

        services.TryAddScoped<IStrategyControlRoomReadModelProvider, StrategyControlRoomReadModelProvider>();
        return services;
    }

    private sealed class StrategyControlRoomReadModelOptionsRegistration;
}
