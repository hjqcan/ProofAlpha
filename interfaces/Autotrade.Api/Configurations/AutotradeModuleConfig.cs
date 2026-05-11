using Autotrade.Hosting;
using Autotrade.Infra.BackgroundJobs.Core;

namespace Autotrade.Api.Configurations;

public static class AutotradeModuleConfig
{
    private const string DefaultConnectionStringName = "AutotradeDatabase";

    public static WebApplicationBuilder AddAutotradeModuleConfiguration(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (!IsModuleRegistrationEnabled(builder.Configuration))
        {
            builder.Services.AddAutotradeModuleInventory(AutotradeModuleInventory.Disabled);
            return builder;
        }

        builder.Services.AddAutotradeModules(
            builder.Configuration,
            builder.Environment,
            options =>
            {
                options.ConnectionStringName = DefaultConnectionStringName;
                options.RegisterHangfireCore = true;
                options.EnableEventBusDashboard = builder.Environment.EnvironmentName is "Development" or "Staging";
            });
        builder.Services.AddHostedService<AutotradeRecurringJobsHostedService>();

        return builder;
    }

    public static WebApplication UseAutotradeModuleConfiguration(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        if (!IsModuleRegistrationEnabled(app.Configuration))
        {
            return app;
        }

        app.UseHangfireDashboardWithConfig(app.Configuration);

        return app;
    }

    private static bool IsModuleRegistrationEnabled(IConfiguration configuration)
    {
        return bool.TryParse(configuration["AutotradeApi:EnableModules"], out var enabled) && enabled;
    }
}
