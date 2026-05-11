using Autotrade.Application.Configuration;
using Autotrade.Llm;
using Autotrade.SelfImprove.Application;
using Autotrade.SelfImprove.Application.Contract;
using Autotrade.SelfImprove.Application.Episodes;
using Autotrade.SelfImprove.Application.GeneratedStrategies;
using Autotrade.SelfImprove.Application.Llm;
using Autotrade.SelfImprove.Application.Proposals;
using Autotrade.SelfImprove.Application.Python;
using Autotrade.SelfImprove.Infra.Data.Repositories;
using Autotrade.Strategy.Application.Engine;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Autotrade.SelfImprove.Infra.CrossCutting.IoC;

public static class SelfImproveServiceCollectionExtensions
{
    public static IServiceCollection AddSelfImproveServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<SelfImproveOptions>(configuration.GetSection(SelfImproveOptions.SectionName));
        services.Configure<ConfigurationMutationOptions>(
            configuration.GetSection(ConfigurationMutationOptions.SectionName));

        services.AddScoped<IConfigurationMutationService, JsonConfigurationMutationService>();
        services.AddScoped<IStrategyEpisodeBuilder, StrategyEpisodeBuilder>();
        services.AddScoped<IProposalValidator, ProposalValidator>();
        services.AddScoped<IGeneratedStrategyPackageService, GeneratedStrategyPackageService>();
        services.AddScoped<ISelfImproveService, SelfImproveService>();

        services.AddScoped<IImprovementRunRepository, ImprovementRunRepository>();
        services.AddScoped<IStrategyEpisodeRepository, StrategyEpisodeRepository>();
        services.AddScoped<IStrategyMemoryRepository, StrategyMemoryRepository>();
        services.AddScoped<IImprovementProposalRepository, ImprovementProposalRepository>();
        services.AddScoped<IParameterPatchRepository, ParameterPatchRepository>();
        services.AddScoped<IGeneratedStrategyVersionRepository, GeneratedStrategyVersionRepository>();
        services.AddScoped<IPromotionGateResultRepository, PromotionGateResultRepository>();
        services.AddScoped<IPatchOutcomeRepository, PatchOutcomeRepository>();

        services.AddOpenAiCompatibleLlmJsonClient(configuration.GetSection($"{SelfImproveOptions.SectionName}:Llm"));
        services.AddScoped<Autotrade.SelfImprove.Application.Contract.Llm.ILLmClient, OpenAiCompatibleLlmClient>();
        services.AddSingleton<IPythonStrategyRuntime, OutOfProcessPythonStrategyRuntime>();
        services.AddSingleton<IPythonStrategyAdapterFactory, PythonStrategyAdapterFactory>();
        services.AddSingleton<IStrategyRegistrationProvider, GeneratedStrategyRegistrationProvider>();

        return services;
    }
}
