using Autotrade.MarketData.Application.Catalog;
using Autotrade.MarketData.Application.Contract.Catalog;
using Autotrade.MarketData.Application.Contract.OrderBook;
using Autotrade.MarketData.Application.Contract.Repositories;
using Autotrade.MarketData.Application.Contract.Snapshots;
using Autotrade.MarketData.Application.Contract.Spot;
using Autotrade.MarketData.Application.Contract.Windows;
using Autotrade.MarketData.Application.OrderBook;
using Autotrade.MarketData.Application.Snapshots;
using Autotrade.MarketData.Application.Spot;
using Autotrade.MarketData.Application.WebSocket.Clob;
using Autotrade.MarketData.Application.WebSocket.Options;
using Autotrade.MarketData.Application.WebSocket.Rtds;
using Autotrade.MarketData.Application.Windows;
using Autotrade.MarketData.Infra.Data.Repositories;
using Autotrade.Polymarket.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Autotrade.MarketData.Infra.CrossCutting.IoC;

/// <summary>
/// MarketData 模块的 DI 注册扩展。
/// </summary>
public static class MarketDataServiceCollectionExtensions
{
    /// <summary>
    /// 添加 MarketData 模块服务。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="configuration">配置。</param>
    /// <returns>服务集合。</returns>
    public static IServiceCollection AddMarketDataServices(this IServiceCollection services, IConfiguration configuration)
    {
        // 配置选项
        services.Configure<PolymarketWebSocketOptions>(
            configuration.GetSection(PolymarketWebSocketOptions.SectionName));
        services.Configure<SpotPriceStoreOptions>(
            configuration.GetSection(SpotPriceStoreOptions.SectionName));
        services.Configure<RtdsSpotPriceFeedOptions>(
            configuration.GetSection(RtdsSpotPriceFeedOptions.SectionName));
        services.Configure<MarketWindowSpecOptions>(
            configuration.GetSection(MarketWindowSpecOptions.SectionName));

        // Gamma（市场元数据）
        services.AddPolymarketGammaClient(configuration);
        services.Configure<MarketCatalogSyncOptions>(
            configuration.GetSection(MarketCatalogSyncOptions.SectionName));

        // WebSocket 客户端（单例，长连接）
        services.AddSingleton<IClobMarketClient, ClobMarketClient>();
        services.AddSingleton<IRtdsClient, RtdsClient>();

        // 本地订单簿存储（单例，内存状态）
        services.AddSingleton<LocalOrderBookStore>();
        services.AddSingleton<ILocalOrderBookStore>(sp => sp.GetRequiredService<LocalOrderBookStore>());
        // 跨上下文只读接口
        services.AddSingleton<IOrderBookReader>(sp => sp.GetRequiredService<LocalOrderBookStore>());
        services.AddSingleton<ISpotPriceStore, InMemorySpotPriceStore>();
        services.AddSingleton<ISpotPriceFeed, RtdsSpotPriceFeed>();

        // 订单簿同步器（单例，管理快照+增量同步）
        services.AddSingleton<IOrderBookSynchronizer, OrderBookSynchronizer>();

        // 市场目录（单例，内存缓存）
        services.AddSingleton<IMarketCatalog, MarketCatalog>();
        services.AddSingleton<IMarketCatalogReader, MarketCatalogReader>();
        services.AddSingleton<IMarketWindowSpecProvider, MarketWindowSpecProvider>();
        services.AddSingleton<IMarketDataSnapshotReader, MarketDataSnapshotReader>();

        // 市场目录同步（Gamma API -> MarketCatalog + 持久化）
        services.AddScoped<IMarketRepository, EfMarketRepository>();

        // 订单簿订阅管理（由策略引擎驱动）
        services.AddSingleton<IOrderBookSubscriptionService, OrderBookSubscriptionService>();

        return services;
    }
}
