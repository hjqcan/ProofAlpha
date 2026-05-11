using Autotrade.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Autotrade.Cli.HostedServices;

public sealed class AutotradeHostedService(
    IAutotradeDatabaseDiagnostics databaseDiagnostics,
    ILogger<AutotradeHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Autotrade starting.");

        var diagnostics = await databaseDiagnostics.CheckConnectionsAsync(stoppingToken).ConfigureAwait(false);
        foreach (var diagnostic in diagnostics)
        {
            if (diagnostic.Failure is not null)
            {
                logger.LogWarning(
                    diagnostic.Failure,
                    "Database connectivity check failed for {Context}.",
                    diagnostic.Name);
                continue;
            }

            logger.LogInformation(
                "Database connectivity check: {Context} -> {CanConnect}",
                diagnostic.Name,
                diagnostic.CanConnect);
        }

        logger.LogInformation("Autotrade started. Press Ctrl+C to exit.");

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken).ConfigureAwait(false);
        }
    }
}
