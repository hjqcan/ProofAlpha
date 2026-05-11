using Autotrade.Domain.Abstractions.EventBus;
using Autotrade.Polymarket.Extensions;
using Autotrade.Trading.Application.Compliance;
using Autotrade.Trading.Application.Audit;
using Autotrade.Trading.Application.Contract.Audit;
using Autotrade.Trading.Application.Contract.Compliance;
using Autotrade.Trading.Application.Contract.Execution;
using Autotrade.Trading.Application.Contract.Accounts;
using Autotrade.Trading.Application.Contract.Repositories;
using Autotrade.Trading.Application.Contract.Risk;
using Autotrade.Trading.Application.Contract.UserEvents;
using Autotrade.Trading.Application.EventHandlers;
using Autotrade.Trading.Application.Execution;
using Autotrade.Trading.Application.Maintenance;
using Autotrade.Trading.Application.Risk;
using Autotrade.Trading.Application.UserEvents;
using Autotrade.Trading.Domain.Events;
using Autotrade.Trading.Domain.Events.Converters;
using Autotrade.Trading.Infra.CrossCutting.IoC.Events;
using Autotrade.Trading.Infra.Data.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetDevPack.Messaging;

namespace Autotrade.Trading.Infra.CrossCutting.IoC;

/// <summary>
/// Trading 服务依赖注入扩展。
/// </summary>
public static class TradingServiceCollectionExtensions
{
    /// <summary>
    /// 添加 Trading 模块服务（包含执行引擎）。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="configuration">配置。</param>
    /// <returns>服务集合。</returns>
    public static IServiceCollection AddTradingServices(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment? environment = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // 0. 注册 Polymarket 客户端（Trading 的 LiveExecution / Reconciliation 依赖）
        services.AddPolymarketClobClient(configuration);
        services.AddPolymarketDataClient(configuration);

        // 1. 绑定配置
        services.Configure<ExecutionOptions>(
            configuration.GetSection(ExecutionOptions.SectionName));

        services.Configure<LiveArmingOptions>(
            configuration.GetSection(LiveArmingOptions.SectionName));

        services.Configure<ComplianceOptions>(
            configuration.GetSection(ComplianceOptions.SectionName));

        services.Configure<ComplianceStrategyEngineOptions>(
            configuration.GetSection(ComplianceStrategyEngineOptions.SectionName));

        services.Configure<UserOrderEventSourceOptions>(
            configuration.GetSection(UserOrderEventSourceOptions.SectionName));

        services.Configure<PaperTradingOptions>(
            configuration.GetSection(PaperTradingOptions.SectionName));

        services.Configure<RiskOptions>(
            configuration.GetSection(RiskOptions.SectionName));

        services.Configure<RiskCapitalOptions>(
            configuration.GetSection(RiskCapitalOptions.SectionName));

        services.Configure<KillSwitchControlOptions>(
            configuration.GetSection(KillSwitchControlOptions.SectionName));

        // 1.1 绑定账户同步配置
        services.Configure<AccountSyncOptions>(
            configuration.GetSection(AccountSyncOptions.SectionName));

        // 2. 注册幂等性存储（单例 - 跨请求共享状态）
        services.AddSingleton<IIdempotencyStore, InMemoryIdempotencyStore>();
        services.AddSingleton<ILiveArmingStateStore, FileLiveArmingStateStore>();

        // 3. 注册订单状态跟踪器（单例 - 跨请求共享状态）
        services.AddSingleton<IOrderStateTracker, InMemoryOrderStateTracker>();

        // 4. 注册订单限制验证器
        services.AddScoped<OrderLimitValidator>();
        services.AddScoped<IComplianceGuard, ComplianceGuard>();

        // 5. 风控模块
        services.AddSingleton<RiskStateStore>();
        services.AddSingleton<RiskMetrics>();

        // 5.1 外部账户快照存储（跨 scope 共享）
        services.AddSingleton<ExternalAccountSnapshotStore>();

        // 5.1.1 Trading 账户引导器（HostedService 与 CLI 复用）
        services.AddSingleton<TradingAccountBootstrapper>();

        // 5.2 注册账户同步服务（同步外部快照并对账）
        services.AddScoped<IAccountSyncService, AccountSyncService>();

        // 5.3 注册风控资金提供者
        // Live 模式使用 EffectiveRiskCapitalProvider（min(RiskLimit, ExternalBalance)）
        // Paper 模式使用 InMemoryRiskCapitalProvider（直接使用配置值）
        services.AddSingleton<InMemoryRiskCapitalProvider>();
        services.AddSingleton<EffectiveRiskCapitalProvider>();
        services.AddSingleton<IRiskCapitalProvider>(sp =>
        {
            var executionOptions = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<ExecutionOptions>>().Value;
            return executionOptions.Mode == ExecutionMode.Live
                ? sp.GetRequiredService<EffectiveRiskCapitalProvider>()
                : sp.GetRequiredService<InMemoryRiskCapitalProvider>();
        });

        // 风控事件仓储：使用 EF Core 持久化到数据库
        services.AddSingleton<IRiskEventRepository, EfRiskEventRepository>();

        services.AddSingleton<IRiskManager, RiskManager>();
        services.AddScoped<IRiskDrilldownService, RiskDrilldownService>();

        // 6.1 注册订单/成交/事件/持仓仓储（用于审计和导出）
        services.AddScoped<IOrderRepository, EfOrderRepository>();
        services.AddScoped<ITradeRepository, EfTradeRepository>();
        services.AddScoped<IOrderEventRepository, EfOrderEventRepository>();
        services.AddScoped<IPositionRepository, EfPositionRepository>();
        services.AddScoped<ITradingAccountProvisioner, EfTradingAccountProvisioner>();
        services.AddScoped<ITradingMaintenanceRepository, EfTradingMaintenanceRepository>();

        // 6.2 注册订单审计日志器
        services.AddScoped<IOrderAuditLogger, OrderAuditLogger>();

        // 6.3 注册领域事件处理器（通过 BaseDbContext.Commit() 自动分发）
        services.AddScoped<IDomainEventHandler<OrderAcceptedEvent>, OrderAcceptedEventHandler>();
        services.AddScoped<IDomainEventHandler<OrderFilledEvent>, OrderFilledEventHandler>();
        services.AddScoped<IDomainEventHandler<OrderCancelledEvent>, OrderCancelledEventHandler>();
        services.AddScoped<IDomainEventHandler<OrderExpiredEvent>, OrderExpiredEventHandler>();
        services.AddScoped<IDomainEventHandler<OrderRejectedEvent>, OrderRejectedEventHandler>();
        services.AddScoped<IDomainEventHandler<TradeExecutedEvent>, TradeExecutedEventHandler>();
        services.AddSingleton<IIntegrationDtoConverter, TradingIntegrationDtoConverter>();
        if (ShouldUseInMemoryEventBus(configuration, environment))
        {
            services.AddTransient<TradingIntegrationEventSink>();
        }

        // 6. 注册执行服务
        // Paper 订单存储必须是 Singleton，因为需要跨请求共享订单状态
        services.AddSingleton<PaperOrderStore>();
        services.AddSingleton<TradingAccountContext>();
        services.AddScoped<LiveExecutionService>();
        services.AddScoped<ILiveArmingService, LiveArmingService>();
        services.AddScoped<PaperExecutionService>();
        services.AddSingleton<IUserOrderEventSource, ClobUserOrderEventSource>();

        // 7. 注册执行模式工厂
        services.AddScoped<ExecutionModeFactory>();

        // 8. 注册 IExecutionService（通过工厂解析）
        services.AddScoped<IExecutionService>(sp =>
        {
            var factory = sp.GetRequiredService<ExecutionModeFactory>();
            return factory.Create();
        });

        // 9. 注册交易数据保留配置（Worker 在 BackgroundJobs 项目）
        services.Configure<TradingRetentionOptions>(
            configuration.GetSection(TradingRetentionOptions.SectionName));

        return services;
    }

    private static bool ShouldUseInMemoryEventBus(IConfiguration configuration, IHostEnvironment? environment)
    {
        var configured = configuration.GetValue<bool?>("EventBus:UseInMemory");
        if (configured.HasValue)
        {
            return configured.Value;
        }

        return environment is not null
            && (environment.IsDevelopment()
                || string.Equals(environment.EnvironmentName, "Test", StringComparison.OrdinalIgnoreCase)
                || string.Equals(environment.EnvironmentName, "Testing", StringComparison.OrdinalIgnoreCase));
    }
}
