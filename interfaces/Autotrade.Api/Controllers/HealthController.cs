using System.Reflection;
using Autotrade.Api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Autotrade.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class HealthController(
    HealthCheckService healthCheckService,
    IHostEnvironment environment,
    ILogger<HealthController> logger) : ControllerBase
{
    private static readonly string ApiVersion =
        typeof(HealthController).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "unknown";

    [HttpGet]
    public ActionResult<ApiHealthResponse> Get()
    {
        logger.LogDebug("Health endpoint requested.");

        return Ok(new ApiHealthResponse(
            Status: "Healthy",
            Application: "Autotrade.Api",
            Environment: environment.EnvironmentName,
            TimestampUtc: DateTimeOffset.UtcNow,
            Version: ApiVersion));
    }

    [HttpGet("detailed")]
    public async Task<ActionResult<ApiDetailedHealthResponse>> GetDetailed(CancellationToken cancellationToken)
    {
        var report = await healthCheckService.CheckHealthAsync(cancellationToken).ConfigureAwait(false);
        var entries = report.Entries.Select(entry => new ApiHealthEntryResponse(
            Name: entry.Key,
            Status: entry.Value.Status.ToString(),
            Description: entry.Value.Description,
            Duration: entry.Value.Duration));

        var response = new ApiDetailedHealthResponse(
            Status: report.Status.ToString(),
            Application: "Autotrade.Api",
            Environment: environment.EnvironmentName,
            TimestampUtc: DateTimeOffset.UtcNow,
            Version: ApiVersion,
            Entries: entries.ToArray());

        return report.Status == HealthStatus.Healthy
            ? Ok(response)
            : StatusCode(StatusCodes.Status503ServiceUnavailable, response);
    }
}
