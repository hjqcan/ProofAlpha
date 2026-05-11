using Autotrade.EventBus.Extensions;
using Autotrade.Infra.BackgroundJobs.Core;
using Autotrade.MarketData.Infra.BackgroundJobs;
using Autotrade.MarketData.Infra.CrossCutting.IoC;
using Autotrade.MarketData.Infra.Data.Context;
using Autotrade.OpportunityDiscovery.Infra.BackgroundJobs;
using Autotrade.OpportunityDiscovery.Infra.CrossCutting.IoC;
using Autotrade.OpportunityDiscovery.Infra.Data.Context;
using Autotrade.SelfImprove.Infra.BackgroundJobs;
using Autotrade.SelfImprove.Infra.CrossCutting.IoC;
using Autotrade.SelfImprove.Infra.Data.Context;
using Autotrade.Strategy.Infra.BackgroundJobs;
using Autotrade.Strategy.Infra.CrossCutting.IoC;
using Autotrade.Strategy.Infra.Data.Context;
using Autotrade.Trading.Infra.BackgroundJobs;
using Autotrade.Trading.Infra.CrossCutting.IoC;
using Autotrade.Trading.Infra.Data.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Autotrade.Hosting;

public static class AutotradeModuleServiceCollectionExtensions
{
    private static readonly IAutotradeModuleRegistration[] DefaultModules =
    [
        new TradingModuleRegistration(),
        new MarketDataModuleRegistration(),
        new StrategyModuleRegistration(),
        new SelfImproveModuleRegistration(),
        new OpportunityDiscoveryModuleRegistration()
    ];

    public static IServiceCollection AddAutotradeModules(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment,
        Action<AutotradeModuleRegistrationOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        var options = new AutotradeModuleRegistrationOptions();
        configure?.Invoke(options);

        var connectionString = ResolveConnectionStringIfNeeded(configuration, options);

        services.AddAutotradeDomainEventDispatcher();
        services.TryAddScoped<IAutotradeDatabaseDiagnostics, AutotradeDatabaseDiagnostics>();

        if (options.RegisterEventBus)
        {
            services.AddAutotradeEventBus(
                configuration,
                environment,
                options.ConnectionStringName,
                options.EnableEventBusDashboard);
        }

        if (options.RegisterHangfireCore)
        {
            services.AddHangfireCore(configuration, options.ConnectionStringName);
        }

        var modules = DefaultModules
            .Select(module => module.Register(services, configuration, environment, options, connectionString))
            .ToArray();

        services.AddSingleton<IAutotradeModuleInventory>(
            new AutotradeModuleInventory(
                enabled: true,
                options.ConnectionStringName,
                eventBusRegistered: options.RegisterEventBus,
                hangfireRegistered: options.RegisterHangfireCore,
                persistenceRegistered: options.RegisterDataContexts,
                modules));

        return services;
    }

    public static IServiceCollection AddAutotradeModuleInventory(
        this IServiceCollection services,
        IAutotradeModuleInventory inventory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(inventory);

        services.AddSingleton(inventory);
        services.TryAddScoped<IAutotradeDatabaseDiagnostics, AutotradeDatabaseDiagnostics>();
        return services;
    }

    private static string? ResolveConnectionStringIfNeeded(
        IConfiguration configuration,
        AutotradeModuleRegistrationOptions options)
    {
        if (!options.RegisterDataContexts && !options.RegisterHangfireCore)
        {
            return null;
        }

        var connectionString = configuration.GetConnectionString(options.ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"Missing connection string: ConnectionStrings:{options.ConnectionStringName}");
        }

        return connectionString;
    }

    private static void AddPostgresDbContext<TContext>(
        IServiceCollection services,
        string? connectionString,
        string migrationsHistoryTable)
        where TContext : DbContext
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("A connection string is required to register DbContexts.");
        }

        services.AddDbContext<TContext>(
            options => options.UseNpgsql(
                connectionString,
                npgsql => npgsql.MigrationsHistoryTable(migrationsHistoryTable)));
    }

    private abstract class AutotradeModuleRegistrationBase : IAutotradeModuleRegistration
    {
        public abstract string Name { get; }

        public abstract string DataContextName { get; }

        public AutotradeModuleDescriptor Register(
            IServiceCollection services,
            IConfiguration configuration,
            IHostEnvironment environment,
            AutotradeModuleRegistrationOptions options,
            string? connectionString)
        {
            if (options.RegisterDataContexts)
            {
                RegisterDataContext(services, connectionString);
            }

            if (options.RegisterApplicationServices)
            {
                RegisterApplicationServices(services, configuration, environment);
            }

            if (options.RegisterBackgroundJobServices)
            {
                RegisterBackgroundJobs(services, configuration);
            }

            return new AutotradeModuleDescriptor(
                Name,
                DataContextName,
                options.RegisterDataContexts,
                options.RegisterApplicationServices,
                options.RegisterBackgroundJobServices);
        }

        protected abstract void RegisterDataContext(IServiceCollection services, string? connectionString);

        protected abstract void RegisterApplicationServices(
            IServiceCollection services,
            IConfiguration configuration,
            IHostEnvironment environment);

        protected abstract void RegisterBackgroundJobs(IServiceCollection services, IConfiguration configuration);
    }

    private sealed class TradingModuleRegistration : AutotradeModuleRegistrationBase
    {
        public override string Name => "Trading";

        public override string DataContextName => nameof(TradingContext);

        protected override void RegisterDataContext(IServiceCollection services, string? connectionString)
            => AddPostgresDbContext<TradingContext>(
                services,
                connectionString,
                TradingContext.MigrationsHistoryTable);

        protected override void RegisterApplicationServices(
            IServiceCollection services,
            IConfiguration configuration,
            IHostEnvironment environment)
            => services.AddTradingServices(configuration, environment);

        protected override void RegisterBackgroundJobs(IServiceCollection services, IConfiguration configuration)
            => services.AddTradingBackgroundJobs(configuration);
    }

    private sealed class MarketDataModuleRegistration : AutotradeModuleRegistrationBase
    {
        public override string Name => "MarketData";

        public override string DataContextName => nameof(MarketDataContext);

        protected override void RegisterDataContext(IServiceCollection services, string? connectionString)
            => AddPostgresDbContext<MarketDataContext>(
                services,
                connectionString,
                MarketDataContext.MigrationsHistoryTable);

        protected override void RegisterApplicationServices(
            IServiceCollection services,
            IConfiguration configuration,
            IHostEnvironment environment)
            => services.AddMarketDataServices(configuration);

        protected override void RegisterBackgroundJobs(IServiceCollection services, IConfiguration configuration)
            => services.AddMarketDataBackgroundJobs(configuration);
    }

    private sealed class StrategyModuleRegistration : AutotradeModuleRegistrationBase
    {
        public override string Name => "Strategy";

        public override string DataContextName => nameof(StrategyContext);

        protected override void RegisterDataContext(IServiceCollection services, string? connectionString)
            => AddPostgresDbContext<StrategyContext>(
                services,
                connectionString,
                StrategyContext.MigrationsHistoryTable);

        protected override void RegisterApplicationServices(
            IServiceCollection services,
            IConfiguration configuration,
            IHostEnvironment environment)
            => services.AddStrategyServices(configuration);

        protected override void RegisterBackgroundJobs(IServiceCollection services, IConfiguration configuration)
            => services.AddStrategyBackgroundJobs(configuration);
    }

    private sealed class SelfImproveModuleRegistration : AutotradeModuleRegistrationBase
    {
        public override string Name => "SelfImprove";

        public override string DataContextName => nameof(SelfImproveContext);

        protected override void RegisterDataContext(IServiceCollection services, string? connectionString)
            => AddPostgresDbContext<SelfImproveContext>(
                services,
                connectionString,
                SelfImproveContext.MigrationsHistoryTable);

        protected override void RegisterApplicationServices(
            IServiceCollection services,
            IConfiguration configuration,
            IHostEnvironment environment)
            => services.AddSelfImproveServices(configuration);

        protected override void RegisterBackgroundJobs(IServiceCollection services, IConfiguration configuration)
            => services.AddSelfImproveBackgroundJobs(configuration);
    }

    private sealed class OpportunityDiscoveryModuleRegistration : AutotradeModuleRegistrationBase
    {
        public override string Name => "OpportunityDiscovery";

        public override string DataContextName => nameof(OpportunityDiscoveryContext);

        protected override void RegisterDataContext(IServiceCollection services, string? connectionString)
            => AddPostgresDbContext<OpportunityDiscoveryContext>(
                services,
                connectionString,
                OpportunityDiscoveryContext.MigrationsHistoryTable);

        protected override void RegisterApplicationServices(
            IServiceCollection services,
            IConfiguration configuration,
            IHostEnvironment environment)
            => services.AddOpportunityDiscoveryServices(configuration);

        protected override void RegisterBackgroundJobs(IServiceCollection services, IConfiguration configuration)
            => services.AddOpportunityDiscoveryBackgroundJobs(configuration);
    }
}
