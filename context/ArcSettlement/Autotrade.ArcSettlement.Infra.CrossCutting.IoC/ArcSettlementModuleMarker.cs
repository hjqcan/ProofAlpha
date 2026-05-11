using Autotrade.ArcSettlement.Application.Configuration;
using Autotrade.ArcSettlement.Application.Contract.Configuration;
using Autotrade.ArcSettlement.Application.Contract.Signals;
using Autotrade.ArcSettlement.Application.Proofs;
using Autotrade.ArcSettlement.Application.Signals;
using Autotrade.ArcSettlement.Infra.Data.Signals;
using Autotrade.ArcSettlement.Infra.Evm.Signals;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Autotrade.ArcSettlement.Infra.CrossCutting.IoC;

public sealed class ArcSettlementModuleMarker;

public static class ArcSettlementServiceCollectionExtensions
{
    public static IServiceCollection AddArcSettlementServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<ArcSettlementOptions>(
            configuration.GetSection(ArcSettlementOptions.SectionName));

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<IArcProofHashService, ArcProofHashService>();
        services.TryAddScoped<IArcProofRedactionGuard, ArcProofRedactionGuard>();
        services.TryAddScoped<IArcProofExportService, ArcProofExportService>();
        services.TryAddScoped<IArcUtilityMetricsCalculator, ArcUtilityMetricsCalculator>();
        services.TryAddScoped<IArcSettlementSecretSource, EnvironmentArcSettlementSecretSource>();
        services.TryAddScoped<ArcSettlementOptionsValidator>();
        services.TryAddScoped<IArcSignalPublicationStore, JsonFileArcSignalPublicationStore>();
        services.TryAddScoped<IArcHardhatSignalPublisherProcessRunner, HardhatSignalPublisherProcessRunner>();
        services.TryAddScoped<IArcSignalRegistryPublisher, HardhatArcSignalRegistryPublisher>();
        services.TryAddScoped<IArcSignalPublicationService, ArcSignalPublicationService>();

        return services;
    }
}
