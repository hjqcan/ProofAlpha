using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Autotrade.Llm;

public static class LlmServiceCollectionExtensions
{
    public static IServiceCollection AddOpenAiCompatibleLlmJsonClient(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<OpenAiCompatibleLlmOptions>(configuration);
        services.AddHttpClient<ILlmJsonClient, OpenAiCompatibleLlmJsonClient>();
        return services;
    }
}
