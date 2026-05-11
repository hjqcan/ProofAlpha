// ============================================================================
// 配置文件服务
// ============================================================================
// 提供配置文件的读写功能，支持基础配置和覆盖配置的合并。
// 
// 配置优先级（从低到高）：
// 1. basePath（appsettings.json）
// 2. overridePath（appsettings.local.json 或 --config 指定的文件）
// ============================================================================

using System.Text.Json;
using System.Text.Json.Nodes;

namespace Autotrade.Cli.Config;

/// <summary>
/// 配置文件服务。
/// 提供 JSON 配置文件的读写功能，支持层级路径访问和配置合并。
/// </summary>
public sealed class ConfigFileService
{
    /// <summary>
    /// 基础配置文件路径。
    /// </summary>
    private readonly string _basePath;

    /// <summary>
    /// 覆盖配置文件路径（优先级更高）。
    /// </summary>
    private readonly string _overridePath;

    /// <summary>
    /// 初始化配置文件服务。
    /// </summary>
    /// <param name="basePath">基础配置文件路径。</param>
    /// <param name="overridePath">覆盖配置文件路径。</param>
    public ConfigFileService(string basePath, string overridePath)
    {
        _basePath = basePath ?? throw new ArgumentNullException(nameof(basePath));
        _overridePath = overridePath ?? throw new ArgumentNullException(nameof(overridePath));
    }

    /// <summary>
    /// 读取合并后的配置值。
    /// </summary>
    /// <param name="path">配置路径（使用冒号分隔层级，如 "StrategyEngine:Enabled"）。</param>
    /// <returns>配置值节点，不存在则返回 null。</returns>
    public JsonNode? GetValue(string path)
    {
        var merged = LoadMerged();
        return GetNode(merged, path);
    }

    /// <summary>
    /// 写入配置值到覆盖配置文件。
    /// </summary>
    /// <param name="path">配置路径。</param>
    /// <param name="rawValue">原始值字符串（会自动解析为合适的 JSON 类型）。</param>
    public void SetValue(string path, string rawValue)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Path cannot be empty.", nameof(path));
        }

        var overrideNode = LoadFile(_overridePath) as JsonObject ?? new JsonObject();
        var valueNode = ParseValue(rawValue);

        SetNode(overrideNode, path, valueNode);
        StampConfigVersionIfNeeded(overrideNode, path);
        WriteSafe(_overridePath, overrideNode.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void StampConfigVersionIfNeeded(JsonObject root, string changedPath)
    {
        if (string.IsNullOrWhiteSpace(changedPath))
        {
            return;
        }

        // 如果用户本身就在写 ConfigVersion，不再自动覆盖
        if (changedPath.EndsWith(":ConfigVersion", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(changedPath, "StrategyEngine:ConfigVersion", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var segments = changedPath.Split(':', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            return;
        }

        string? versionPath = null;

        if (segments[0].Equals("StrategyEngine", StringComparison.OrdinalIgnoreCase))
        {
            versionPath = "StrategyEngine:ConfigVersion";
        }
        else if (segments[0].Equals("Strategies", StringComparison.OrdinalIgnoreCase) && segments.Length >= 2)
        {
            versionPath = $"Strategies:{segments[1]}:ConfigVersion";
        }

        if (versionPath is null)
        {
            return;
        }

        var stamp = $"v{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
        SetNode(root, versionPath, JsonValue.Create(stamp)!);
    }

    /// <summary>
    /// 加载并合并基础配置和覆盖配置。
    /// </summary>
    private JsonNode LoadMerged()
    {
        var baseNode = LoadFile(_basePath) as JsonObject ?? new JsonObject();
        var overrideNode = LoadFile(_overridePath) as JsonObject ?? new JsonObject();

        return Merge(baseNode, overrideNode);
    }

    /// <summary>
    /// 加载 JSON 文件。
    /// </summary>
    private static JsonNode? LoadFile(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        var json = File.ReadAllText(path);
        return JsonNode.Parse(json);
    }

    /// <summary>
    /// 深度合并两个 JSON 对象。
    /// 覆盖配置中的值会覆盖基础配置中的同名值。
    /// </summary>
    private static JsonNode Merge(JsonNode baseNode, JsonNode overrideNode)
    {
        if (baseNode is JsonObject baseObj && overrideNode is JsonObject overrideObj)
        {
            var result = new JsonObject();

            // 先复制基础配置
            foreach (var kvp in baseObj)
            {
                result[kvp.Key] = kvp.Value is null ? null : kvp.Value.DeepClone();
            }

            // 用覆盖配置覆盖或合并
            foreach (var kvp in overrideObj)
            {
                if (kvp.Value is JsonObject overrideChild && result[kvp.Key] is JsonObject baseChild)
                {
                    result[kvp.Key] = Merge(baseChild, overrideChild);
                }
                else
                {
                    result[kvp.Key] = kvp.Value is null ? null : kvp.Value.DeepClone();
                }
            }

            return result;
        }

        return overrideNode.DeepClone();
    }

    /// <summary>
    /// 根据路径获取 JSON 节点。
    /// </summary>
    private static JsonNode? GetNode(JsonNode? root, string path)
    {
        if (root is null)
        {
            return null;
        }

        var segments = path.Split(':', StringSplitOptions.RemoveEmptyEntries);
        JsonNode? current = root;
        foreach (var segment in segments)
        {
            if (current is JsonObject obj && obj.TryGetPropertyValue(segment, out var next))
            {
                current = next;
            }
            else
            {
                return null;
            }
        }

        return current;
    }

    /// <summary>
    /// 根据路径设置 JSON 节点（会自动创建中间节点）。
    /// </summary>
    private static void SetNode(JsonObject root, string path, JsonNode value)
    {
        var segments = path.Split(':', StringSplitOptions.RemoveEmptyEntries);
        JsonObject current = root;

        for (var i = 0; i < segments.Length; i++)
        {
            var segment = segments[i];
            if (i == segments.Length - 1)
            {
                current[segment] = value;
                return;
            }

            if (current[segment] is not JsonObject child)
            {
                child = new JsonObject();
                current[segment] = child;
            }

            current = child;
        }
    }

    /// <summary>
    /// 解析原始值字符串为 JSON 节点。
    /// 支持自动检测：布尔、整数、小数、JSON 对象/数组、字符串。
    /// </summary>
    private static JsonNode ParseValue(string rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return JsonValue.Create(string.Empty) ?? JsonValue.Create(string.Empty)!;
        }

        var trimmed = rawValue.Trim();

        // 检测 JSON 对象或数组
        if ((trimmed.StartsWith("{") && trimmed.EndsWith("}")) ||
            (trimmed.StartsWith("[") && trimmed.EndsWith("]")))
        {
            return JsonNode.Parse(trimmed) ?? new JsonObject();
        }

        // 检测布尔值
        if (bool.TryParse(trimmed, out var boolValue))
        {
            return JsonValue.Create(boolValue) ?? JsonValue.Create(boolValue)!;
        }

        // 检测整数
        if (int.TryParse(trimmed, out var intValue))
        {
            return JsonValue.Create(intValue) ?? JsonValue.Create(intValue)!;
        }

        // 检测小数
        if (decimal.TryParse(trimmed, out var decimalValue))
        {
            return JsonValue.Create(decimalValue) ?? JsonValue.Create(decimalValue)!;
        }

        // 默认为字符串
        return JsonValue.Create(trimmed) ?? JsonValue.Create(trimmed)!;
    }

    /// <summary>
    /// 安全写入文件（使用临时文件和原子替换）。
    /// </summary>
    private static void WriteSafe(string path, string content)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = path + ".tmp";
        File.WriteAllText(tempPath, content);
        if (File.Exists(path))
        {
            File.Replace(tempPath, path, path + ".bak", ignoreMetadataErrors: true);
        }
        else
        {
            File.Move(tempPath, path);
        }
    }
}
