using Autotrade.ArcSettlement.Application.Configuration;
using Autotrade.ArcSettlement.Application.Access;
using Autotrade.ArcSettlement.Application.Contract.Access;
using Autotrade.ArcSettlement.Application.Contract.Configuration;
using Autotrade.ArcSettlement.Application.Contract.Signals;
using Autotrade.ArcSettlement.Application.Contract.Performance;
using Autotrade.ArcSettlement.Application.Contract.Provenance;
using Autotrade.ArcSettlement.Application.Performance;
using Autotrade.ArcSettlement.Application.Proofs;
using Autotrade.ArcSettlement.Application.Provenance;
using Autotrade.ArcSettlement.Application.Signals;
using Autotrade.ArcSettlement.Infra.Data.Access;
using Autotrade.ArcSettlement.Infra.Data.Performance;
using Autotrade.ArcSettlement.Infra.Data.Provenance;
using Autotrade.ArcSettlement.Infra.Data.Signals;
using Autotrade.ArcSettlement.Infra.Evm.Performance;
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
        services.TryAddScoped<IArcStrategyProvenanceStore, JsonFileArcStrategyProvenanceStore>();
        services.TryAddScoped<IArcStrategyProvenanceService, ArcStrategyProvenanceService>();
        services.TryAddScoped<IArcUtilityMetricsCalculator, ArcUtilityMetricsCalculator>();
        services.TryAddScoped<IArcSubscriptionPlanService, ArcSubscriptionPlanService>();
        services.TryAddScoped<IArcSubscriptionSyncService, ArcSubscriptionSyncService>();
        services.TryAddScoped<IArcAccessDecisionService, ArcAccessDecisionService>();
        services.TryAddScoped<IArcSettlementSecretSource, EnvironmentArcSettlementSecretSource>();
        services.TryAddScoped<ArcSettlementOptionsValidator>();
        services.TryAddScoped<IArcEntitlementMirrorStore, JsonFileArcEntitlementMirrorStore>();
        services.TryAddScoped<IArcStrategyAccessReader, ArcStrategyAccessReader>();
        services.TryAddScoped<IArcSignalPublicationStore, JsonFileArcSignalPublicationStore>();
        services.TryAddScoped<IArcHardhatSignalPublisherProcessRunner, HardhatSignalPublisherProcessRunner>();
        services.TryAddScoped<IArcSignalRegistryPublisher, HardhatArcSignalRegistryPublisher>();
        services.TryAddScoped<IArcSignalPublicationService, ArcSignalPublicationService>();
        services.TryAddScoped<IArcPerformanceOutcomeBuilder, ArcPerformanceOutcomeBuilder>();
        services.TryAddScoped<IArcPerformanceOutcomeStore, JsonFileArcPerformanceOutcomeStore>();
        services.TryAddScoped<IArcHardhatPerformanceLedgerProcessRunner, HardhatPerformanceLedgerProcessRunner>();
        services.TryAddScoped<IArcPerformanceLedgerPublisher, HardhatArcPerformanceLedgerPublisher>();
        services.TryAddScoped<IArcPerformanceRecorder, ArcPerformanceRecorder>();

        return services;
    }
}
