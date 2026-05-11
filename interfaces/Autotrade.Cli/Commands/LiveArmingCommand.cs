using System.Text.Json;
using System.Text.Json.Serialization;
using Autotrade.Trading.Application.Contract.Execution;
using Microsoft.Extensions.DependencyInjection;

namespace Autotrade.Cli.Commands;

public static class LiveArmingCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    public static async Task<int> StatusAsync(CommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        using var scope = context.Host.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ILiveArmingService>();
        var status = await service.GetStatusAsync().ConfigureAwait(false);
        WriteStatus(context, status);
        return status.IsArmed ? ExitCodes.Success : ExitCodes.GeneralError;
    }

    public static async Task<int> ArmAsync(
        CommandContext context,
        string? actor,
        string? reason,
        string? confirmationText)
    {
        ArgumentNullException.ThrowIfNull(context);

        using var scope = context.Host.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ILiveArmingService>();
        var result = await service
            .ArmAsync(new LiveArmingRequest(ResolveActor(actor), reason, confirmationText))
            .ConfigureAwait(false);
        WriteResult(context, result);
        return result.Accepted ? ExitCodes.Success : ExitCodes.GeneralError;
    }

    public static async Task<int> DisarmAsync(
        CommandContext context,
        string? actor,
        string? reason,
        string? confirmationText)
    {
        ArgumentNullException.ThrowIfNull(context);

        using var scope = context.Host.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ILiveArmingService>();
        var result = await service
            .DisarmAsync(new LiveDisarmingRequest(ResolveActor(actor), reason, confirmationText))
            .ConfigureAwait(false);
        WriteResult(context, result);
        return result.Accepted ? ExitCodes.Success : ExitCodes.GeneralError;
    }

    private static void WriteStatus(CommandContext context, LiveArmingStatus status)
    {
        if (context.JsonOutput)
        {
            Console.WriteLine(JsonSerializer.Serialize(status, JsonOptions));
            return;
        }

        Console.WriteLine($"Live arming: {status.State}");
        Console.WriteLine($"Reason: {status.Reason}");
        Console.WriteLine($"Config version: {status.ConfigVersion}");
        if (status.Evidence is not null)
        {
            Console.WriteLine($"Evidence: {status.Evidence.EvidenceId}");
            Console.WriteLine($"Operator: {status.Evidence.Operator}");
            Console.WriteLine($"Armed at: {status.Evidence.ArmedAtUtc:O}");
            Console.WriteLine($"Expires at: {status.Evidence.ExpiresAtUtc:O}");
            Console.WriteLine($"Open notional: {status.Evidence.RiskSummary.OpenNotional:0.####}");
            Console.WriteLine($"Open orders: {status.Evidence.RiskSummary.OpenOrders}");
        }

        foreach (var reason in status.BlockingReasons)
        {
            Console.WriteLine($"- {reason}");
        }
    }

    private static void WriteResult(CommandContext context, LiveArmingResult result)
    {
        if (context.JsonOutput)
        {
            Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
            return;
        }

        Console.WriteLine($"{result.Status}: {result.Message}");
        WriteStatus(context, result.CurrentStatus);
    }

    private static string ResolveActor(string? actor)
        => string.IsNullOrWhiteSpace(actor)
            ? Environment.UserName
            : actor.Trim();
}
