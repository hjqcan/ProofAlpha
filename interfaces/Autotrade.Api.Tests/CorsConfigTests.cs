using Autotrade.Api.Configurations;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Autotrade.Api.Tests;

public sealed class CorsConfigTests
{
    [Fact]
    public void AddCorsConfigurationUsesLocalOriginsWhenConfigurationIsEmpty()
    {
        var builder = WebApplication.CreateBuilder();

        builder.AddCorsConfiguration();

        using var provider = builder.Services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<CorsOptions>>().Value;
        var policy = options.GetPolicy(CorsConfig.ElectronCorsPolicy);

        Assert.NotNull(policy);
        Assert.False(policy.AllowAnyOrigin);
        Assert.Contains("http://localhost:3000", policy.Origins);
        Assert.Contains("http://localhost:5173", policy.Origins);
        Assert.Contains("http://127.0.0.1:3000", policy.Origins);
        Assert.Contains("http://127.0.0.1:5173", policy.Origins);
    }
}
