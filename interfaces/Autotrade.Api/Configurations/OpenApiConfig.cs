namespace Autotrade.Api.Configurations;

public static class OpenApiConfig
{
    public static WebApplicationBuilder AddOpenApiConfiguration(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddOpenApi();
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new()
            {
                Title = "Autotrade Control Room API",
                Version = "v1",
                Description = "Browser-facing API for Autotrade operations, market data, order books, and control commands."
            });
        });

        return builder;
    }

    public static WebApplication MapOpenApiSetup(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
            app.UseSwagger();
            app.UseSwaggerUI(options =>
            {
                options.SwaggerEndpoint("/swagger/v1/swagger.json", "Autotrade Control Room API v1");
                options.RoutePrefix = "swagger";
                options.DocumentTitle = "Autotrade API";
            });
        }

        return app;
    }
}
