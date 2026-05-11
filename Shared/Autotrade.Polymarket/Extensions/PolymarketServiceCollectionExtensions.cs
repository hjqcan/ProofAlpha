using Autotrade.Polymarket.Abstractions;
using Autotrade.Polymarket.Http;
using Autotrade.Polymarket.Observability;
using Autotrade.Polymarket.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Autotrade.Polymarket.Extensions;

/// <summary>
/// Polymarket 客户端 DI 扩展方法。
/// </summary>
public static class PolymarketServiceCollectionExtensions
{
    /// <summary>
    /// 注册 Polymarket CLOB 客户端（HttpClientFactory + 鉴权/限流/Polly + 可观测性）。
    /// </summary>
    public static IServiceCollection AddPolymarketClobClient(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PolymarketClobOptions>(configuration.GetSection(PolymarketClobOptions.SectionName));
        services.Configure<PolymarketResilienceOptions>(configuration.GetSection(PolymarketResilienceOptions.SectionName));
        services.Configure<PolymarketRateLimitOptions>(configuration.GetSection(PolymarketRateLimitOptions.SectionName));

        // 可观测性
        services.AddSingleton<PolymarketMetrics>();
        services.AddSingleton<IPolymarketOrderSigner, PolymarketOrderSigner>();

        // 委托处理器
        services.AddTransient<PolymarketAuthHandler>();
        services.AddTransient<PolymarketRateLimitHandler>();
        services.AddTransient<PolymarketLoggingHandler>();
        services.AddTransient<PolymarketIdempotencyHandler>();
        services.AddTransient<PolymarketMetricsHandler>();

        services
            .AddHttpClient<IPolymarketClobClient, PolymarketClobClient>((sp, client) =>
            {
                var opt = sp.GetRequiredService<IOptions<PolymarketClobOptions>>().Value;
                // BaseAddress 需要尾随斜杠以正确处理相对路径
                client.BaseAddress = new Uri(opt.Host.TrimEnd('/') + "/");
                client.Timeout = opt.Timeout;
            })
            .ConfigurePrimaryHttpMessageHandler(sp =>
            {
                var opt = sp.GetRequiredService<IOptions<PolymarketClobOptions>>().Value;
                return new SocketsHttpHandler
                {
                    UseProxy = !opt.DisableProxy
                };
            })
            // 执行顺序：Metrics -> Logging -> RateLimit -> Idempotency -> Auth -> HttpClient
            .AddHttpMessageHandler<PolymarketMetricsHandler>()
            .AddHttpMessageHandler<PolymarketLoggingHandler>()
            .AddHttpMessageHandler<PolymarketRateLimitHandler>()
            .AddHttpMessageHandler<PolymarketIdempotencyHandler>()
            .AddHttpMessageHandler<PolymarketAuthHandler>();

        return services;
    }

    /// <summary>
    /// 注册 Polymarket Gamma 客户端（只读市场元数据）。
    /// </summary>
    public static IServiceCollection AddPolymarketGammaClient(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PolymarketGammaOptions>(configuration.GetSection(PolymarketGammaOptions.SectionName));
        services.Configure<PolymarketResilienceOptions>(configuration.GetSection(PolymarketResilienceOptions.SectionName));

        services.AddHttpClient<IPolymarketGammaClient, PolymarketGammaClient>((sp, client) =>
        {
            var opt = sp.GetRequiredService<IOptions<PolymarketGammaOptions>>().Value;
            client.BaseAddress = new Uri(opt.Host.TrimEnd('/') + "/");
            client.Timeout = opt.Timeout;
        });

        return services;
    }

    /// <summary>
    /// 注册 Polymarket CLOB 客户端（带 Action 配置，便于测试）。
    /// </summary>
    public static IServiceCollection AddPolymarketClobClient(
        this IServiceCollection services,
        Action<PolymarketClobOptions> configureClobOptions,
        Action<PolymarketResilienceOptions>? configureResilienceOptions = null,
        Action<PolymarketRateLimitOptions>? configureRateLimitOptions = null)
    {
        services.Configure(configureClobOptions);
        if (configureResilienceOptions is not null)
        {
            services.Configure(configureResilienceOptions);
        }
        else
        {
            services.Configure<PolymarketResilienceOptions>(_ => { });
        }

        if (configureRateLimitOptions is not null)
        {
            services.Configure(configureRateLimitOptions);
        }
        else
        {
            services.Configure<PolymarketRateLimitOptions>(_ => { });
        }

        // 可观测性
        services.AddSingleton<PolymarketMetrics>();
        services.AddSingleton<IPolymarketOrderSigner, PolymarketOrderSigner>();

        // 委托处理器
        services.AddTransient<PolymarketAuthHandler>();
        services.AddTransient<PolymarketRateLimitHandler>();
        services.AddTransient<PolymarketLoggingHandler>();
        services.AddTransient<PolymarketIdempotencyHandler>();
        services.AddTransient<PolymarketMetricsHandler>();

        services
            .AddHttpClient<IPolymarketClobClient, PolymarketClobClient>((sp, client) =>
            {
                var opt = sp.GetRequiredService<IOptions<PolymarketClobOptions>>().Value;
                // BaseAddress 需要尾随斜杠以正确处理相对路径
                client.BaseAddress = new Uri(opt.Host.TrimEnd('/') + "/");
                client.Timeout = opt.Timeout;
            })
            .ConfigurePrimaryHttpMessageHandler(sp =>
            {
                var opt = sp.GetRequiredService<IOptions<PolymarketClobOptions>>().Value;
                return new SocketsHttpHandler
                {
                    UseProxy = !opt.DisableProxy
                };
            })
            .AddHttpMessageHandler<PolymarketMetricsHandler>()
            .AddHttpMessageHandler<PolymarketLoggingHandler>()
            .AddHttpMessageHandler<PolymarketRateLimitHandler>()
            .AddHttpMessageHandler<PolymarketIdempotencyHandler>()
            .AddHttpMessageHandler<PolymarketAuthHandler>();

        return services;
    }

    /// <summary>
    /// 注册 Polymarket Data API 客户端（用于查询持仓等用户数据）。
    /// Data API 是公开 API，无需签名认证。
    /// </summary>
    public static IServiceCollection AddPolymarketDataClient(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PolymarketDataOptions>(configuration.GetSection(PolymarketDataOptions.SectionName));
        services.Configure<PolymarketResilienceOptions>(configuration.GetSection(PolymarketResilienceOptions.SectionName));

        services.AddHttpClient<IPolymarketDataClient, PolymarketDataClient>((sp, client) =>
        {
            var opt = sp.GetRequiredService<IOptions<PolymarketDataOptions>>().Value;
            client.BaseAddress = new Uri(opt.Host.TrimEnd('/') + "/");
            client.Timeout = opt.Timeout;
        });

        return services;
    }

    /// <summary>
    /// 注册 Polymarket Data API 客户端（带 Action 配置，便于测试）。
    /// </summary>
    public static IServiceCollection AddPolymarketDataClient(
        this IServiceCollection services,
        Action<PolymarketDataOptions> configureDataOptions,
        Action<PolymarketResilienceOptions>? configureResilienceOptions = null)
    {
        services.Configure(configureDataOptions);

        if (configureResilienceOptions is not null)
        {
            services.Configure(configureResilienceOptions);
        }
        else
        {
            services.Configure<PolymarketResilienceOptions>(_ => { });
        }

        services.AddHttpClient<IPolymarketDataClient, PolymarketDataClient>((sp, client) =>
        {
            var opt = sp.GetRequiredService<IOptions<PolymarketDataOptions>>().Value;
            client.BaseAddress = new Uri(opt.Host.TrimEnd('/') + "/");
            client.Timeout = opt.Timeout;
        });

        return services;
    }
}
