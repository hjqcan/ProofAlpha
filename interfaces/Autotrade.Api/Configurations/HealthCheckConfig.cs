using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Autotrade.Api.Configurations;

public static class HealthCheckConfig
{
    public static WebApplicationBuilder AddHealthCheckConfiguration(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddHealthChecks()
            .AddCheck(
                "self",
                () => HealthCheckResult.Healthy("Autotrade API process is running."),
                tags: ["live", "ready"]);

        return builder;
    }

    public static WebApplication MapHealthCheckEndpoints(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains("live", StringComparer.OrdinalIgnoreCase)
        });

        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains("ready", StringComparer.OrdinalIgnoreCase)
        });

        return app;
    }
}
