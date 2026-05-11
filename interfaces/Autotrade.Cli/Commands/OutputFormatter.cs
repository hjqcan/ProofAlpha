// ============================================================================
// 输出格式化器
// ============================================================================
// 统一 CLI 输出格式，支持 JSON 和人类可读格式。
// ============================================================================

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Autotrade.Cli.Commands;

/// <summary>
/// 标准化 JSON 输出结构。
/// </summary>
public sealed class StandardOutput
{
    [JsonPropertyName("success")]
    public bool Success { get; init; }

    [JsonPropertyName("exitCode")]
    public int ExitCode { get; init; }

    [JsonPropertyName("message")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Message { get; init; }

    [JsonPropertyName("errorCode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ErrorCode { get; init; }

    [JsonPropertyName("data")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? Data { get; init; }

    [JsonPropertyName("timestamp")]
    public string Timestamp { get; init; } = DateTimeOffset.UtcNow.ToString("O");
}

/// <summary>
/// 输出格式化器。
/// 提供统一的 CLI 输出格式化功能。
/// </summary>
public static class OutputFormatter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// 输出命令结果。
    /// </summary>
    public static void WriteResult(CommandResult result, GlobalOptions options)
    {
        if (options.JsonOutput)
        {
            WriteJson(result);
        }
        else
        {
            WriteHuman(result, options);
        }
    }

    /// <summary>
    /// 输出成功结果。
    /// </summary>
    public static void WriteSuccess(string message, object? data, GlobalOptions options)
    {
        var result = new CommandResult
        {
            ExitCode = ExitCodes.Success,
            Message = message,
            Data = data
        };
        WriteResult(result, options);
    }

    /// <summary>
    /// 输出错误结果。
    /// </summary>
    public static void WriteError(string message, string? errorCode, GlobalOptions options, int exitCode = ExitCodes.GeneralError)
    {
        var result = new CommandResult
        {
            ExitCode = exitCode,
            Message = message,
            ErrorCode = errorCode
        };
        WriteResult(result, options);
    }

    /// <summary>
    /// 输出数据（纯数据，无包装）。
    /// </summary>
    public static void WriteData(object data, GlobalOptions options)
    {
        if (options.JsonOutput)
        {
            Console.WriteLine(JsonSerializer.Serialize(data, JsonOptions));
        }
        else
        {
            Console.WriteLine(data);
        }
    }

    private static void WriteJson(CommandResult result)
    {
        var output = new StandardOutput
        {
            Success = result.ExitCode == ExitCodes.Success,
            ExitCode = result.ExitCode,
            Message = result.Message,
            ErrorCode = result.ErrorCode,
            Data = result.Data
        };
        Console.WriteLine(JsonSerializer.Serialize(output, JsonOptions));
    }

    private static void WriteHuman(CommandResult result, GlobalOptions options)
    {
        var isError = result.ExitCode != ExitCodes.Success;
        var writer = isError ? Console.Error : Console.Out;

        if (!string.IsNullOrWhiteSpace(result.Message))
        {
            if (isError && !options.NoColor)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                writer.WriteLine($"错误: {result.Message}");
                Console.ResetColor();
            }
            else
            {
                writer.WriteLine(result.Message);
            }
        }

        if (result.Data is not null)
        {
            Console.WriteLine(JsonSerializer.Serialize(result.Data, JsonOptions));
        }
    }
}
