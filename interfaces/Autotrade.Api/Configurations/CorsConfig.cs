namespace Autotrade.Api.Configurations;

public static class CorsConfig
{
    public const string ElectronCorsPolicy = "AutotradeElectron";

    private static readonly string[] DefaultLocalOrigins =
    [
        "http://localhost:3000",
        "http://localhost:5173",
        "http://127.0.0.1:3000",
        "http://127.0.0.1:5173"
    ];

    public static WebApplicationBuilder AddCorsConfiguration(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var configuredOrigins = builder.Configuration
            .GetSection("AutotradeApi:Cors:AllowedOrigins")
            .GetChildren()
            .Select(section => section.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .ToArray();
        var allowedOrigins = configuredOrigins.Length > 0
            ? configuredOrigins
            : DefaultLocalOrigins;

        builder.Services.AddCors(options =>
        {
            options.AddPolicy(ElectronCorsPolicy, policy =>
            {
                policy.WithOrigins(allowedOrigins)
                    .AllowAnyHeader()
                    .AllowAnyMethod();
            });
        });

        return builder;
    }
}
