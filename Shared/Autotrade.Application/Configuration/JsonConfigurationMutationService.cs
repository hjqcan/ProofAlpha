using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;

namespace Autotrade.Application.Configuration;

public sealed class JsonConfigurationMutationService : IConfigurationMutationService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private static readonly string[] ForbiddenPathTokens =
    {
        "apikey",
        "api_key",
        "secret",
        "privatekey",
        "private_key",
        "passphrase",
        "connectionstrings",
        "execution:mode",
        "compliance:",
        "riskcontrol:",
        "killswitch",
        "liveautoapplyenabled"
    };

    private readonly ConfigurationMutationOptions _options;

    public JsonConfigurationMutationService(IOptions<ConfigurationMutationOptions> options)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    public Task<ConfigurationMutationResult> MutateAsync(
        ConfigurationMutationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var errors = new List<string>();
        var warnings = new List<string>();
        if (request.Patches.Count == 0)
        {
            errors.Add("At least one configuration patch is required.");
        }

        var basePath = ResolvePath(_options.BasePath, preferDevProject: false);
        var overridePath = ResolvePath(_options.OverridePath, preferDevProject: true);
        var baseRoot = LoadJsonObject(basePath);
        var overrideRoot = LoadJsonObject(overridePath);
        var effectiveRoot = Merge(baseRoot, overrideRoot);
        var rollback = new List<object>();
        var diff = new List<object>();
        var configVersion = $"si-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";

        foreach (var patch in request.Patches)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidatePatchPath(patch.Path, errors);

            JsonNode? valueNode = null;
            try
            {
                valueNode = JsonNode.Parse(patch.ValueJson);
            }
            catch (JsonException ex)
            {
                errors.Add($"{patch.Path}: ValueJson is not valid JSON. {ex.Message}");
            }

            if (valueNode is null)
            {
                continue;
            }

            var current = GetNode(effectiveRoot, patch.Path);
            if (current is not null && !IsCompatibleJsonKind(current, valueNode))
            {
                errors.Add($"{patch.Path}: New value kind {valueNode.GetValueKind()} is incompatible with existing kind {current.GetValueKind()}.");
                continue;
            }

            diff.Add(new
            {
                patch.Path,
                before = current?.DeepClone(),
                after = valueNode.DeepClone(),
                patch.Reason
            });
            rollback.Add(new
            {
                patch.Path,
                value = current?.DeepClone()
            });

            if (errors.Count == 0)
            {
                SetNode(overrideRoot, patch.Path, valueNode.DeepClone());
                StampConfigVersion(overrideRoot, patch.Path, configVersion);
            }
        }

        if (errors.Count > 0)
        {
            return Task.FromResult(new ConfigurationMutationResult(
                false,
                request.DryRun,
                configVersion,
                JsonSerializer.Serialize(diff, JsonOptions),
                JsonSerializer.Serialize(rollback, JsonOptions),
                errors,
                warnings));
        }

        if (!request.DryRun)
        {
            WriteSafe(overridePath, overrideRoot.ToJsonString(JsonOptions));
        }

        return Task.FromResult(new ConfigurationMutationResult(
            true,
            request.DryRun,
            configVersion,
            JsonSerializer.Serialize(diff, JsonOptions),
            JsonSerializer.Serialize(rollback, JsonOptions),
            errors,
            warnings));
    }

    private void ValidatePatchPath(string path, ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            errors.Add("Patch path cannot be empty.");
            return;
        }

        var normalized = path.Trim().ToLowerInvariant();
        if (_options.AllowedPathPrefixes.Count > 0
            && !_options.AllowedPathPrefixes.Any(prefix => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
        {
            errors.Add($"{path}: Path is not in the SelfImprove configuration patch whitelist.");
        }

        if (ForbiddenPathTokens.Any(token => normalized.Contains(token, StringComparison.Ordinal)))
        {
            errors.Add($"{path}: Path is permanently forbidden for SelfImprove mutation.");
        }

        if (path.EndsWith(":Enabled", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"{path}: SelfImprove cannot toggle strategy Enabled flags through parameter patches.");
        }
    }

    private static bool IsCompatibleJsonKind(JsonNode current, JsonNode next)
    {
        var currentKind = current.GetValueKind();
        var nextKind = next.GetValueKind();
        if (currentKind == nextKind)
        {
            return true;
        }

        return IsNumber(currentKind) && IsNumber(nextKind);
    }

    private static bool IsNumber(JsonValueKind kind)
    {
        return kind is JsonValueKind.Number;
    }

    private static JsonObject LoadJsonObject(string path)
    {
        if (!File.Exists(path))
        {
            return new JsonObject();
        }

        var node = JsonNode.Parse(File.ReadAllText(path));
        return node as JsonObject ?? new JsonObject();
    }

    private static string ResolvePath(string path, bool preferDevProject)
    {
        if (Path.IsPathRooted(path))
        {
            return Path.GetFullPath(path);
        }

        if (preferDevProject)
        {
            var devProjectDir = TryGetCliProjectDir(AppContext.BaseDirectory);
            if (devProjectDir is not null)
            {
                return Path.GetFullPath(Path.Combine(devProjectDir, path));
            }
        }

        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, path));
    }

    private static string? TryGetCliProjectDir(string start)
    {
        var current = new DirectoryInfo(start);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Autotrade.Cli.csproj")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return null;
    }

    private static JsonObject Merge(JsonObject baseObj, JsonObject overrideObj)
    {
        var result = new JsonObject();
        foreach (var kvp in baseObj)
        {
            result[kvp.Key] = kvp.Value?.DeepClone();
        }

        foreach (var kvp in overrideObj)
        {
            if (kvp.Value is JsonObject overrideChild && result[kvp.Key] is JsonObject baseChild)
            {
                result[kvp.Key] = Merge(baseChild, overrideChild);
            }
            else
            {
                result[kvp.Key] = kvp.Value?.DeepClone();
            }
        }

        return result;
    }

    private static JsonNode? GetNode(JsonNode? root, string path)
    {
        var current = root;
        foreach (var segment in path.Split(':', StringSplitOptions.RemoveEmptyEntries))
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

    private static void SetNode(JsonObject root, string path, JsonNode value)
    {
        var segments = path.Split(':', StringSplitOptions.RemoveEmptyEntries);
        var current = root;
        for (var index = 0; index < segments.Length; index++)
        {
            var segment = segments[index];
            if (index == segments.Length - 1)
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

    private static void StampConfigVersion(JsonObject root, string changedPath, string configVersion)
    {
        if (changedPath.EndsWith(":ConfigVersion", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var segments = changedPath.Split(':', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length >= 2 && segments[0].Equals("Strategies", StringComparison.OrdinalIgnoreCase))
        {
            SetNode(root, $"Strategies:{segments[1]}:ConfigVersion", JsonValue.Create(configVersion)!);
        }
        else if (segments.Length > 0 && segments[0].Equals("StrategyEngine", StringComparison.OrdinalIgnoreCase))
        {
            SetNode(root, "StrategyEngine:ConfigVersion", JsonValue.Create(configVersion)!);
        }
    }

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

internal static class JsonNodeKindExtensions
{
    public static JsonValueKind GetValueKind(this JsonNode node)
    {
        using var document = JsonDocument.Parse(node.ToJsonString());
        return document.RootElement.ValueKind;
    }
}
