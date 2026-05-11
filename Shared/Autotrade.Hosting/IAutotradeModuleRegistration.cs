using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Autotrade.Hosting;

public interface IAutotradeModuleRegistration
{
    string Name { get; }

    string DataContextName { get; }

    AutotradeModuleDescriptor Register(
        IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment,
        AutotradeModuleRegistrationOptions options,
        string? connectionString);
}
