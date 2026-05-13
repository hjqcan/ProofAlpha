using Autotrade.Llm;
using Autotrade.OpportunityDiscovery.Application;
using Autotrade.OpportunityDiscovery.Application.Contract;
using Autotrade.OpportunityDiscovery.Application.Evidence;
using Autotrade.OpportunityDiscovery.Infra.Data.Repositories;
using Autotrade.OpportunityDiscovery.Infra.Sources;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Autotrade.OpportunityDiscovery.Infra.CrossCutting.IoC;

public static class OpportunityDiscoveryServiceCollectionExtensions
{
    public static IServiceCollection AddOpportunityDiscoveryServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<OpportunityDiscoveryOptions>(
            configuration.GetSection(OpportunityDiscoveryOptions.SectionName));
        services.AddOpenAiCompatibleLlmJsonClient(
            configuration.GetSection($"{OpportunityDiscoveryOptions.SectionName}:Llm"));

        services.AddScoped<IResearchRunRepository, ResearchRunRepository>();
        services.AddScoped<IEvidenceItemRepository, EvidenceItemRepository>();
        services.AddScoped<ISourceProfileRepository, SourceProfileRepository>();
        services.AddScoped<ISourceObservationRepository, SourceObservationRepository>();
        services.AddScoped<IEvidenceSnapshotRepository, EvidenceSnapshotRepository>();
        services.AddScoped<IOpportunityV2Repository, OpportunityV2Repository>();
        services.AddScoped<IMarketOpportunityRepository, MarketOpportunityRepository>();
        services.AddScoped<IOpportunityReviewRepository, OpportunityReviewRepository>();

        services.AddScoped<OpportunityQueryService>();
        services.AddScoped<IOpportunityQueryService>(sp => sp.GetRequiredService<OpportunityQueryService>());
        services.AddScoped<IPublishedOpportunityFeed>(sp => sp.GetRequiredService<OpportunityQueryService>());
        services.AddScoped<ISourceRegistryService, SourceRegistryService>();
        services.AddScoped<IOpportunityEvidenceExplainService, OpportunityEvidenceExplainService>();
        services.AddScoped<IOpportunityScoringService, OpportunityScoringService>();
        services.AddScoped<IExecutableOpportunityPolicyFeed, ExecutableOpportunityPolicyFeed>();
        services.AddScoped<IOpportunityDiscoveryService, OpportunityDiscoveryService>();

        services.AddHttpClient<IEvidenceSource, GdeltDocApiSource>();
        services.AddHttpClient<IEvidenceSource, RssFeedSource>();
        services.AddHttpClient<IEvidenceSource, OpenAiWebSearchSource>();

        return services;
    }
}
