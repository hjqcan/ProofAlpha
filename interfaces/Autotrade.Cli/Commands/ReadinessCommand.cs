using System.Text.Json;
using System.Text.Json.Serialization;
using Autotrade.Application.Readiness;
using Microsoft.Extensions.DependencyInjection;

namespace Autotrade.Cli.Commands;

public static class ReadinessCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    public static async Task<int> ExecuteAsync(CommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var service = context.Services.GetRequiredService<IReadinessReportService>();
        var report = await service.GetReportAsync().ConfigureAwait(false);

        if (context.JsonOutput)
        {
            Console.WriteLine(JsonSerializer.Serialize(report, JsonOptions));
        }
        else
        {
            WriteHumanReadable(report);
        }

        return IsPaperBlocked(report) ? ExitCodes.GeneralError : ExitCodes.Success;
    }

    private static void WriteHumanReadable(ReadinessReport report)
    {
        Console.WriteLine($"Readiness: {report.Status}");
        foreach (var capability in report.Capabilities)
        {
            Console.WriteLine($"- {capability.Capability}: {capability.Status} {capability.Summary}");
        }

        foreach (var check in report.Checks)
        {
            Console.WriteLine($"  [{check.Status}] {check.Id}: {check.Summary}");
            if (check.Status is ReadinessCheckStatus.Blocked or ReadinessCheckStatus.Unhealthy)
            {
                Console.WriteLine($"      Next: {check.RemediationHint}");
            }
        }
    }

    private static bool IsPaperBlocked(ReadinessReport report)
    {
        return report.Capabilities.Any(capability =>
            capability.Capability == ReadinessCapability.PaperTrading &&
            capability.Status == ReadinessOverallStatus.Blocked);
    }
}
