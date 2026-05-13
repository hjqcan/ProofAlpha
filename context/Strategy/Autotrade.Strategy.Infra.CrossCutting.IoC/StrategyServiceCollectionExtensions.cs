using Autotrade.Strategy.Application.Audit;
using Autotrade.Strategy.Application.ControlRoom;
using Autotrade.Strategy.Application.Contract.Strategies;
using Autotrade.Strategy.Application.Decisions;
using Autotrade.Strategy.Application.Engine;
using Autotrade.Strategy.Application.Observations;
using Autotrade.Strategy.Application.Parameters;
using Autotrade.Strategy.Application.Persistence;
using Autotrade.Strategy.Application.Promotion;
using Autotrade.Strategy.Application.RunReports;
using Autotrade.Strategy.Application.RunSessions;
using Autotrade.Strategy.Application.Strategies.DualLeg;
using Autotrade.Strategy.Application.Strategies.Endgame;
using Autotrade.Strategy.Application.Strategies.LiquidityMaking;
using Autotrade.Strategy.Application.Strategies.LiquidityPulse;
using Autotrade.Strategy.Application.Strategies.Opportunity;
using Autotrade.Strategy.Application.Strategies.RepricingLag;
using Autotrade.Strategy.Application.Strategies.Volatility;
using Autotrade.Strategy.Application.Orders;
using Autotrade.Strategy.Infra.Data.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Autotrade.Strategy.Infra.CrossCutting.IoC;

public static class StrategyServiceCollectionExtensions
{
    public static IServiceCollection AddStrategyServices(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddStrategyControlRoomReadModel(configuration);

        services.Configure<StrategyRetentionOptions>(
            configuration.GetSection(StrategyRetentionOptions.SectionName));

        services.Configure<StrategyObservationOptions>(
            configuration.GetSection(StrategyObservationOptions.SectionName));

        services.Configure<PaperPromotionChecklistOptions>(
            configuration.GetSection(PaperPromotionChecklistOptions.SectionName));

        services.AddSingleton<StrategyRuntimeStore>();
        services.AddSingleton<StrategyOrderRegistry>();
        services.AddSingleton<IMarketSnapshotProvider, MarketSnapshotProvider>();

        services.AddSingleton<IStrategyFactory, StrategyFactory>();

        // DualLegArbitrage 策略注册
        services.AddSingleton(new StrategyRegistration(
            "dual_leg_arbitrage",
            "DualLegArbitrage",
            typeof(DualLegArbitrageStrategy),
            DualLegArbitrageOptions.SectionName,
            (sp, context) => ActivatorUtilities.CreateInstance<DualLegArbitrageStrategy>(sp, context),
            sp =>
            {
                var options = sp.GetRequiredService<IOptionsMonitor<DualLegArbitrageOptions>>().CurrentValue;
                return new StrategyOptionsSnapshot(
                    "dual_leg_arbitrage",
                    options.Enabled,
                    options.ConfigVersion);
            }));

        // EndgameSweep 策略注册
        services.AddSingleton(new StrategyRegistration(
            "endgame_sweep",
            "EndgameSweep",
            typeof(EndgameSweepStrategy),
            EndgameSweepOptions.SectionName,
            (sp, context) => ActivatorUtilities.CreateInstance<EndgameSweepStrategy>(sp, context),
            sp =>
            {
                var options = sp.GetRequiredService<IOptionsMonitor<EndgameSweepOptions>>().CurrentValue;
                return new StrategyOptionsSnapshot(
                    "endgame_sweep",
                    options.Enabled,
                    options.ConfigVersion);
            }));

        services.AddSingleton(new StrategyRegistration(
            "liquidity_pulse",
            "LiquidityPulse",
            typeof(LiquidityPulseStrategy),
            LiquidityPulseOptions.SectionName,
            (sp, context) => ActivatorUtilities.CreateInstance<LiquidityPulseStrategy>(sp, context),
            sp =>
            {
                var options = sp.GetRequiredService<IOptionsMonitor<LiquidityPulseOptions>>().CurrentValue;
                return new StrategyOptionsSnapshot(
                    "liquidity_pulse",
                    options.Enabled,
                    options.ConfigVersion);
            }));

        services.AddSingleton(new StrategyRegistration(
            "liquidity_maker",
            "LiquidityMaker",
            typeof(LiquidityMakerStrategy),
            LiquidityMakerOptions.SectionName,
            (sp, context) => ActivatorUtilities.CreateInstance<LiquidityMakerStrategy>(sp, context),
            sp =>
            {
                var options = sp.GetRequiredService<IOptionsMonitor<LiquidityMakerOptions>>().CurrentValue;
                return new StrategyOptionsSnapshot(
                    "liquidity_maker",
                    options.Enabled,
                    options.ConfigVersion);
            }));

        services.AddSingleton(new StrategyRegistration(
            "micro_volatility_scalper",
            "MicroVolatilityScalper",
            typeof(MicroVolatilityScalperStrategy),
            MicroVolatilityScalperOptions.SectionName,
            (sp, context) => ActivatorUtilities.CreateInstance<MicroVolatilityScalperStrategy>(sp, context),
            sp =>
            {
                var options = sp.GetRequiredService<IOptionsMonitor<MicroVolatilityScalperOptions>>().CurrentValue;
                return new StrategyOptionsSnapshot(
                    "micro_volatility_scalper",
                    options.Enabled,
                    options.ConfigVersion);
            }));

        services.AddSingleton(new StrategyRegistration(
            "repricing_lag_arbitrage",
            "RepricingLagArbitrage",
            typeof(RepricingLagArbitrageStrategy),
            RepricingLagArbitrageOptions.SectionName,
            (sp, context) => ActivatorUtilities.CreateInstance<RepricingLagArbitrageStrategy>(sp, context),
            sp =>
            {
                var options = sp.GetRequiredService<IOptionsMonitor<RepricingLagArbitrageOptions>>().CurrentValue;
                return new StrategyOptionsSnapshot(
                    "repricing_lag_arbitrage",
                    options.Enabled,
                    options.ConfigVersion);
            }));

        services.AddSingleton(new StrategyRegistration(
            "llm_opportunity",
            "LlmOpportunity",
            typeof(LlmOpportunityStrategy),
            LlmOpportunityOptions.SectionName,
            (sp, context) => ActivatorUtilities.CreateInstance<LlmOpportunityStrategy>(sp, context),
            sp =>
            {
                var options = sp.GetRequiredService<IOptionsMonitor<LlmOpportunityOptions>>().CurrentValue;
                return new StrategyOptionsSnapshot(
                    "llm_opportunity",
                    options.Enabled,
                    options.ConfigVersion);
            }));

        services.AddScoped<IStrategyDecisionRepository, StrategyDecisionRepository>();
        services.AddScoped<IStrategyDecisionQueryService, StrategyDecisionQueryService>();
        services.AddScoped<IStrategyDecisionLogger, StrategyDecisionLogger>();
        services.AddScoped<IStrategyObservationRepository, StrategyObservationRepository>();
        services.AddScoped<IStrategyObservationLogger, StrategyObservationLogger>();

        services.AddScoped<IPaperRunSessionRepository, PaperRunSessionRepository>();
        services.AddScoped<IPaperRunSessionService, PaperRunSessionService>();
        services.AddScoped<Autotrade.Application.RunSessions.IRunSessionAccessor>(
            sp => sp.GetRequiredService<IPaperRunSessionService>());
        services.AddScoped<IPaperRunReportService, PaperRunReportService>();
        services.AddScoped<IPaperPromotionChecklistService, PaperPromotionChecklistService>();
        services.AddScoped<IDualLegArbitrageReplayRunner, DualLegArbitrageReplayRunner>();

        services.AddScoped<ICommandAuditRepository, CommandAuditRepository>();
        services.AddScoped<ICommandAuditLogger, CommandAuditLogger>();
        services.AddScoped<IAuditTimelineService, AuditTimelineService>();
        services.AddScoped<IReplayExportService, ReplayExportService>();

        services.AddScoped<IStrategyUnitOfWork, StrategyUnitOfWork>();
        services.AddScoped<IStrategyParameterVersionRepository, StrategyParameterVersionRepository>();
        services.AddScoped<IStrategyParameterVersionService, StrategyParameterVersionService>();
        services.AddHostedService<StrategyParameterVersionStartupService>();

        services.AddScoped<IStrategyMaintenanceRepository, StrategyMaintenanceRepository>();

        services.AddSingleton<IStrategyRunStateRepository, StrategyRunStateRepository>();



        return services;
    }
}
