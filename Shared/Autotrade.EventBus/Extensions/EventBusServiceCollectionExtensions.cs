using Autotrade.Domain.Abstractions.EventBus;
using Autotrade.EventBus.CAP;
using Autotrade.EventBus.Converters;
using DotNetCore.CAP;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using NetDevPack.Messaging;
using Npgsql;
using Savorboard.CAP.InMemoryMessageQueue;

namespace Autotrade.EventBus.Extensions;

public static class EventBusServiceCollectionExtensions
{
    public static WebApplicationBuilder AddEventBus(
        this WebApplicationBuilder builder,
        string connectionStringName = "AutotradeDatabase")
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddAutotradeEventBus(
            builder.Configuration,
            builder.Environment,
            connectionStringName,
            enableDashboard: builder.Environment.EnvironmentName is "Development" or "Staging");

        return builder;
    }

    public static IServiceCollection AddAutotradeEventBus(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment,
        string connectionStringName = "AutotradeDatabase",
        bool enableDashboard = false)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        services.AddAutotradeDomainEventDispatcher();
        services.AddSingleton<DomainEventToIntegrationEventConverter>();
        services.AddSingleton<IntegrationDtoConverterRegistry>();
        services.AddScoped<IIntegrationEventPublisher, CapIntegrationEventPublisher>();

        services.AddCap(options =>
        {
            var useInMemory = configuration
                .GetSection("EventBus")
                .GetValue<bool?>("UseInMemory") ?? IsDevelopmentLike(environment);

            if (useInMemory)
            {
                options.UseInMemoryStorage();
                options.UseInMemoryMessageQueue();
            }
            else
            {
                options.UsePostgreSql(opt =>
                {
                    var connectionString = configuration.GetConnectionString(connectionStringName);
                    if (string.IsNullOrWhiteSpace(connectionString))
                    {
                        throw new InvalidOperationException(
                            $"Missing connection string: ConnectionStrings:{connectionStringName}");
                    }

                    opt.DataSource = NpgsqlDataSource.Create(connectionString);
                    opt.Schema = "cap";
                });

                var rabbitMQSection = configuration.GetSection("RabbitMQ");
                if (!rabbitMQSection.Exists())
                {
                    throw new InvalidOperationException(
                        "RabbitMQ configuration is required when EventBus:UseInMemory is false.");
                }

                options.UseRabbitMQ(cfg =>
                {
                    cfg.HostName = rabbitMQSection["Host"] ?? "localhost";
                    cfg.UserName = rabbitMQSection["UserName"] ?? "guest";
                    cfg.Password = rabbitMQSection["Password"] ?? "guest";
                    cfg.VirtualHost = rabbitMQSection["VirtualHost"] ?? "/";
                    cfg.ExchangeName = rabbitMQSection["ExchangeName"] ?? "autotrade.events";

                    var port = rabbitMQSection["Port"];
                    if (!string.IsNullOrWhiteSpace(port) && int.TryParse(port, out var portNumber))
                    {
                        cfg.Port = portNumber;
                    }
                });
            }

            options.FailedRetryCount = 3;
            options.FailedRetryInterval = 60;
            options.FailedThresholdCallback = failed =>
            {
                Console.WriteLine($"CAP message failed: {failed.MessageType}");
            };

            options.EnablePublishParallelSend = true;

            if (enableDashboard && environment.EnvironmentName is "Development" or "Staging")
            {
                options.UseDashboard();
            }

            options.Version = "v1";
            options.SucceedMessageExpiredAfter = 24 * 3600;
            options.FailedMessageExpiredAfter = 15 * 24 * 3600;
            options.ConsumerThreadCount = 1;
        });

        return services;
    }

    public static IServiceCollection AddAutotradeDomainEventDispatcher(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped<IDomainEventDispatcher, InMemoryDomainEventDispatcher>();
        return services;
    }

    private static bool IsDevelopmentLike(IHostEnvironment environment)
    {
        return environment.IsDevelopment()
            || string.Equals(environment.EnvironmentName, "Test", StringComparison.OrdinalIgnoreCase)
            || string.Equals(environment.EnvironmentName, "Testing", StringComparison.OrdinalIgnoreCase);
    }
}
