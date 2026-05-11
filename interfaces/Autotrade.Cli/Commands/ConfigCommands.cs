// ============================================================================
// Config 命令处理器
// ============================================================================

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Autotrade.Cli.Config;
using Autotrade.Cli.Infrastructure;
using Autotrade.Trading.Application.Compliance;
using Autotrade.Trading.Application.Contract.Compliance;
using Autotrade.Trading.Application.Contract.Execution;
using Autotrade.Trading.Application.Contract.Risk;
using Microsoft.Extensions.Options;

namespace Autotrade.Cli.Commands;

/// <summary>
/// 处理 config 相关命令：get、set、validate。
/// </summary>
public static class ConfigCommands
{
    private static readonly IReadOnlyList<string> EnvPrefixes = new[]
    {
        // 允许可选前缀（如果未来决定统一前缀，可在此扩展）
        string.Empty,
        "AUTOTRADE__"
    };

    private static (JsonNode? Node, string Source) GetEffectiveValue(ConfigFileService configService, string path)
    {
        ArgumentNullException.ThrowIfNull(configService);
        if (string.IsNullOrWhiteSpace(path))
        {
            return (null, "unknown");
        }

        // 1) 环境变量覆盖（优先级高于文件）
        // .NET 环境变量映射规则：用 "__" 表示 ":"，例如 StrategyEngine__Enabled
        var envKey = path.Replace(":", "__", StringComparison.Ordinal);
        foreach (var prefix in EnvPrefixes)
        {
            var value = Environment.GetEnvironmentVariable(prefix + envKey);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return (ParseValue(value), "env");
            }
        }

        // 2) 文件（base + override）
        return (configService.GetValue(path), "file");
    }

    private static JsonNode ParseValue(string rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return JsonValue.Create(string.Empty)!;
        }

        var trimmed = rawValue.Trim();

        // JSON object/array
        if ((trimmed.StartsWith("{") && trimmed.EndsWith("}")) ||
            (trimmed.StartsWith("[") && trimmed.EndsWith("]")))
        {
            return JsonNode.Parse(trimmed) ?? new JsonObject();
        }

        if (bool.TryParse(trimmed, out var boolValue))
        {
            return JsonValue.Create(boolValue)!;
        }

        if (int.TryParse(trimmed, out var intValue))
        {
            return JsonValue.Create(intValue)!;
        }

        if (decimal.TryParse(trimmed, out var decimalValue))
        {
            return JsonValue.Create(decimalValue)!;
        }

        return JsonValue.Create(trimmed)!;
    }

    /// <summary>
    /// 读取指定配置路径的值。
    /// </summary>
    public static int Get(ConfigFileService configService, string path, bool json, bool showSource = false)
    {
        // 敏感路径脱敏处理
        if (ConfigSchema.IsSensitive(path))
        {
            if (json)
            {
                Console.WriteLine(JsonSerializer.Serialize(new { value = ConfigSchema.RedactedPlaceholder, sensitive = true }));
            }
            else
            {
                Console.WriteLine(ConfigSchema.RedactedPlaceholder);
            }
            return ExitCodes.Success;
        }

        var (node, source) = GetEffectiveValue(configService, path);

        if (node is null)
        {
            Console.Error.WriteLine("未找到指定配置路径");
            return ExitCodes.NotFound;
        }

        if (json)
        {
            if (showSource)
            {
                var output = new
                {
                    path,
                    value = node,
                    source,
                    known = ConfigSchema.IsKnownPath(path)
                };
                Console.WriteLine(JsonSerializer.Serialize(output, new JsonSerializerOptions { WriteIndented = true }));
            }
            else
            {
                Console.WriteLine(node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            }
        }
        else
        {
            Console.WriteLine(node.ToJsonString());
            if (showSource)
            {
                Console.WriteLine($"  来源: {source}");
                if (ConfigSchema.IsKnownPath(path))
                {
                    var info = ConfigSchema.GetPathInfo(path);
                    Console.WriteLine($"  描述: {info?.Description}");
                }
            }
        }

        return ExitCodes.Success;
    }

    /// <summary>
    /// 写入配置值到配置文件。
    /// </summary>
    public static int Set(ConfigFileService configService, string path, string value, GlobalOptions options)
    {
        // 敏感路径警告
        if (ConfigSchema.IsSensitive(path))
        {
            if (!options.NoColor)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
            }
            Console.WriteLine("⚠️  警告: 此配置路径包含敏感信息，请确保不要在日志中暴露。");
            if (!options.NoColor)
            {
                Console.ResetColor();
            }
        }

        // 类型校验
        if (!ConfigSchema.ValidateType(path, value, out var error))
        {
            Console.Error.WriteLine($"类型校验失败: {error}");
            return ExitCodes.ValidationFailed;
        }

        // 确认机制
        if (!ConfirmationService.ConfirmDestructive($"config set --path {path}", options))
        {
            Console.WriteLine("操作已取消。");
            return ExitCodes.UserCancelled;
        }

        configService.SetValue(path, value);
        Console.WriteLine("✅ 配置已写入。运行中的进程会自动热加载（少数配置可能需要重启）。");
        return ExitCodes.Success;
    }

    /// <summary>
    /// 写入配置值到配置文件（兼容旧接口）。
    /// </summary>
    public static int Set(ConfigFileService configService, string path, string value) =>
        Set(configService, path, value, new GlobalOptions());

    /// <summary>
    /// 校验整个配置文件。
    /// </summary>
    public static int Validate(ConfigFileService configService, bool json)
    {
        var issues = new List<ConfigValidationIssue>();
        var validCount = 0;

        foreach (var (path, info) in ConfigSchema.KnownPaths)
        {
            var (node, source) = GetEffectiveValue(configService, path);

            if (node is null)
            {
                if (info.DefaultValue is null && !info.IsOptional)
                {
                    issues.Add(new ConfigValidationIssue(
                        path,
                        "MISSING",
                        info.Description,
                        source));
                }
                continue;
            }

            var rawValue = node.ToJsonString().Trim('"');
            if (!ConfigSchema.ValidateType(path, rawValue, out var error))
            {
                issues.Add(new ConfigValidationIssue(
                    path,
                    "INVALID_TYPE",
                    info.Description,
                    source,
                    Error: error));
            }
            else
            {
                validCount++;
            }
        }

        AddComplianceValidationIssues(configService, issues);

        if (json)
        {
            var result = new
            {
                valid = issues.Count == 0,
                validCount,
                issueCount = issues.Count,
                issues
            };
            Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            }));
        }
        else
        {
            Console.WriteLine($"配置校验完成: {validCount} 个有效, {issues.Count} 个问题");
            foreach (var issue in issues)
            {
                Console.WriteLine($"  - [{issue.Issue}] {issue.Path}: {issue.Error ?? issue.Description}");
            }
        }

        return issues.Count == 0 ? ExitCodes.Success : ExitCodes.ValidationFailed;
    }

    private static void AddComplianceValidationIssues(ConfigFileService configService, List<ConfigValidationIssue> issues)
    {
        var mode = GetString(configService, "Execution:Mode", "Paper");
        var executionMode = Enum.TryParse<ExecutionMode>(mode, ignoreCase: true, out var parsedMode)
            ? parsedMode
            : ExecutionMode.Paper;
        var guard = new ComplianceGuard(
            Options.Create(new ComplianceOptions
            {
                Enabled = GetBool(configService, "Compliance:Enabled", true),
                GeoKycAllowed = GetBool(configService, "Compliance:GeoKycAllowed", false),
                AllowUnsafeLiveParameters = GetBool(configService, "Compliance:AllowUnsafeLiveParameters", false),
                MinLiveEvaluationIntervalSeconds = GetInt(configService, "Compliance:MinLiveEvaluationIntervalSeconds", 1),
                MaxLiveOrdersPerCycle = GetInt(configService, "Compliance:MaxLiveOrdersPerCycle", 10),
                MaxLiveOpenOrders = GetInt(configService, "Compliance:MaxLiveOpenOrders", 100),
                MaxLiveOpenOrdersPerMarket = GetInt(configService, "Compliance:MaxLiveOpenOrdersPerMarket", 20),
                MinLiveReconciliationIntervalSeconds = GetInt(configService, "Compliance:MinLiveReconciliationIntervalSeconds", 5),
                MaxLiveCapitalPerMarket = GetDecimal(configService, "Compliance:MaxLiveCapitalPerMarket", 0.25m),
                MaxLiveCapitalPerStrategy = GetDecimal(configService, "Compliance:MaxLiveCapitalPerStrategy", 0.50m),
                MaxLiveTotalCapitalUtilization = GetDecimal(configService, "Compliance:MaxLiveTotalCapitalUtilization", 0.80m)
            }),
            Options.Create(new ExecutionOptions
            {
                Mode = executionMode,
                MaxOpenOrdersPerMarket = GetInt(configService, "Execution:MaxOpenOrdersPerMarket", 10),
                ReconciliationIntervalSeconds = GetInt(configService, "Execution:ReconciliationIntervalSeconds", 60)
            }),
            Options.Create(new RiskOptions
            {
                MaxOpenOrders = GetInt(configService, "Risk:MaxOpenOrders", 20),
                MaxCapitalPerMarket = GetDecimal(configService, "Risk:MaxCapitalPerMarket", 0.05m),
                MaxCapitalPerStrategy = GetDecimal(configService, "Risk:MaxCapitalPerStrategy", 0.30m),
                MaxTotalCapitalUtilization = GetDecimal(configService, "Risk:MaxTotalCapitalUtilization", 0.50m)
            }),
            Options.Create(new ComplianceStrategyEngineOptions
            {
                EvaluationIntervalSeconds = GetInt(configService, "StrategyEngine:EvaluationIntervalSeconds", 2),
                MaxOrdersPerCycle = GetInt(configService, "StrategyEngine:MaxOrdersPerCycle", 4)
            }));

        foreach (var issue in guard.CheckConfiguration(executionMode).Issues
                     .Where(issue => issue.BlocksLiveOrders && issue.Severity == ComplianceSeverity.Error))
        {
            issues.Add(new ConfigValidationIssue(
                GetComplianceIssuePath(issue.Code),
                issue.Code == "COMPLIANCE_GEO_KYC_UNCONFIRMED"
                    ? "COMPLIANCE_BLOCKING"
                    : "COMPLIANCE_UNSAFE_LIVE_PARAMETER",
                issue.Code == "COMPLIANCE_GEO_KYC_UNCONFIRMED"
                    ? "Explicit geo/KYC confirmation is required before Live order placement."
                    : "Unsafe Live parameters require Compliance:AllowUnsafeLiveParameters=true.",
                "effective",
                Error: issue.Message,
                Code: issue.Code));
        }
    }

    private static string GetComplianceIssuePath(string code)
    {
        return code switch
        {
            "COMPLIANCE_GEO_KYC_UNCONFIRMED" => "Compliance:GeoKycAllowed",
            "COMPLIANCE_EVALUATION_INTERVAL" => "StrategyEngine:EvaluationIntervalSeconds",
            "COMPLIANCE_MAX_ORDERS_PER_CYCLE" => "StrategyEngine:MaxOrdersPerCycle",
            "COMPLIANCE_MAX_OPEN_ORDERS_PER_MARKET" => "Execution:MaxOpenOrdersPerMarket",
            "COMPLIANCE_RECONCILIATION_INTERVAL" => "Execution:ReconciliationIntervalSeconds",
            "COMPLIANCE_MAX_OPEN_ORDERS" => "Risk:MaxOpenOrders",
            "COMPLIANCE_MAX_CAPITAL_PER_MARKET" => "Risk:MaxCapitalPerMarket",
            "COMPLIANCE_MAX_CAPITAL_PER_STRATEGY" => "Risk:MaxCapitalPerStrategy",
            "COMPLIANCE_MAX_TOTAL_CAPITAL_UTILIZATION" => "Risk:MaxTotalCapitalUtilization",
            _ => "Compliance"
        };
    }

    private static string GetString(ConfigFileService configService, string path, string defaultValue)
    {
        var (node, _) = GetEffectiveValue(configService, path);
        return node?.GetValue<string>() ?? defaultValue;
    }

    private static bool GetBool(ConfigFileService configService, string path, bool defaultValue)
    {
        var (node, _) = GetEffectiveValue(configService, path);
        return node is not null && bool.TryParse(node.ToJsonString().Trim('"'), out var value)
            ? value
            : defaultValue;
    }

    private static int GetInt(ConfigFileService configService, string path, int defaultValue)
    {
        var (node, _) = GetEffectiveValue(configService, path);
        return node is not null && int.TryParse(node.ToJsonString().Trim('"'), out var value)
            ? value
            : defaultValue;
    }

    private static decimal GetDecimal(ConfigFileService configService, string path, decimal defaultValue)
    {
        var (node, _) = GetEffectiveValue(configService, path);
        return node is not null && decimal.TryParse(node.ToJsonString().Trim('"'), out var value)
            ? value
            : defaultValue;
    }

    private sealed record ConfigValidationIssue(
        [property: JsonPropertyName("path")] string Path,
        [property: JsonPropertyName("issue")] string Issue,
        [property: JsonPropertyName("description")] string Description,
        [property: JsonPropertyName("source")] string Source,
        [property: JsonPropertyName("error")] string? Error = null,
        [property: JsonPropertyName("code")] string? Code = null);
}
