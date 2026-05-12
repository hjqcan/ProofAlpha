using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Autotrade.SelfImprove.Application.Contract.GeneratedStrategies;
using Autotrade.SelfImprove.Application.Contract.Proposals;
using Autotrade.SelfImprove.Domain.Entities;
using Autotrade.SelfImprove.Domain.Shared.Enums;
using Microsoft.Extensions.Options;

namespace Autotrade.SelfImprove.Application.GeneratedStrategies;

public interface IGeneratedStrategyPackageService
{
    Task<GeneratedStrategyVersion> CreatePackageAsync(
        Guid proposalId,
        GeneratedStrategySpec spec,
        CancellationToken cancellationToken = default);

    Task<GeneratedStrategyValidationResult> ValidatePackageAsync(
        GeneratedStrategyVersion version,
        CancellationToken cancellationToken = default);
}

public sealed class GeneratedStrategyPackageService : IGeneratedStrategyPackageService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private static readonly string[] RequiredPackageFiles =
    {
        "manifest.json",
        "strategy.py",
        "params.schema.json",
        "tests/test_strategy.py",
        "replay.json",
        "risk_envelope.json"
    };

    private static readonly string[] SafeEnvironmentVariableNames =
    {
        "PATH",
        "Path",
        "PATHEXT",
        "SystemRoot",
        "WINDIR",
        "TEMP",
        "TMP",
        "USERPROFILE",
        "HOME"
    };

    private readonly SelfImproveOptions _options;

    public GeneratedStrategyPackageService(IOptions<SelfImproveOptions> options)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task<GeneratedStrategyVersion> CreatePackageAsync(
        Guid proposalId,
        GeneratedStrategySpec spec,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(spec);
        if (!_options.CodeGen.Enabled)
        {
            throw new InvalidOperationException("SelfImprove code generation is disabled.");
        }

        var versionSuffix = proposalId.ToString("N")[..8];
        var version = $"v{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{versionSuffix}";
        var stagingRoot = Path.Combine(
            ResolveArtifactRoot(),
            SanitizePathSegment(spec.StrategyId),
            version);
        Directory.CreateDirectory(stagingRoot);

        var files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["strategy.py"] = spec.PythonModule,
            ["params.schema.json"] = NormalizeJson(spec.ParameterSchemaJson, "{}"),
            ["tests/test_strategy.py"] = spec.UnitTests,
            ["replay.json"] = NormalizeJson(spec.ReplaySpecJson, "{}"),
            ["risk_envelope.json"] = NormalizeJson(spec.RiskEnvelopeJson, "{}")
        };

        foreach (var (relativePath, content) in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = Path.Combine(stagingRoot, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, content, cancellationToken).ConfigureAwait(false);
        }

        var packageHash = await ComputePackageHashAsync(stagingRoot, cancellationToken).ConfigureAwait(false);
        var manifestJson = BuildManifestJson(spec, version, packageHash);
        await File.WriteAllTextAsync(Path.Combine(stagingRoot, "manifest.json"), manifestJson, cancellationToken)
            .ConfigureAwait(false);
        packageHash = await ComputePackageHashAsync(stagingRoot, cancellationToken).ConfigureAwait(false);
        manifestJson = BuildManifestJson(spec, version, packageHash);
        await File.WriteAllTextAsync(Path.Combine(stagingRoot, "manifest.json"), manifestJson, cancellationToken)
            .ConfigureAwait(false);

        return new GeneratedStrategyVersion(
            proposalId,
            spec.StrategyId,
            version,
            stagingRoot,
            packageHash,
            manifestJson,
            NormalizeJson(spec.RiskEnvelopeJson, "{}"),
            DateTimeOffset.UtcNow);
    }

    public async Task<GeneratedStrategyValidationResult> ValidatePackageAsync(
        GeneratedStrategyVersion version,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(version);
        var gates = new List<GeneratedStrategyGateValidationResult>();

        var staticGate = await ValidateStaticGateAsync(version, cancellationToken).ConfigureAwait(false);
        gates.Add(staticGate);
        if (!staticGate.Passed)
        {
            return BuildValidationResult(version, gates);
        }

        var unitGate = await ValidateUnitTestGateAsync(version, cancellationToken).ConfigureAwait(false);
        gates.Add(unitGate);
        if (!unitGate.Passed)
        {
            return BuildValidationResult(version, gates);
        }

        var replayGate = await ValidateReplayGateAsync(version, cancellationToken).ConfigureAwait(false);
        gates.Add(replayGate);
        return BuildValidationResult(version, gates);
    }

    private async Task<GeneratedStrategyGateValidationResult> ValidateStaticGateAsync(
        GeneratedStrategyVersion version,
        CancellationToken cancellationToken)
    {
        var errors = new List<string>();

        foreach (var relative in RequiredPackageFiles)
        {
            if (!File.Exists(Path.Combine(version.ArtifactRoot, relative)))
            {
                errors.Add($"Missing generated strategy file: {relative}");
            }
        }

        if (errors.Count == 0)
        {
            try
            {
                _ = JsonSerializer.Deserialize<GeneratedStrategyManifest>(
                    await File.ReadAllTextAsync(Path.Combine(version.ArtifactRoot, "manifest.json"), cancellationToken)
                        .ConfigureAwait(false),
                    new JsonSerializerOptions(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true });
            }
            catch (JsonException ex)
            {
                errors.Add($"Generated strategy manifest is invalid JSON: {ex.Message}");
            }
        }

        if (errors.Count == 0)
        {
            errors.AddRange(ValidateJsonFile(version.ArtifactRoot, "params.schema.json"));
            errors.AddRange(ValidateJsonFile(version.ArtifactRoot, "replay.json"));
            errors.AddRange(ValidateJsonFile(version.ArtifactRoot, "risk_envelope.json"));
            errors.AddRange(ValidateRiskEnvelope(version));
        }

        if (errors.Count == 0)
        {
            var hash = await ComputePackageHashAsync(version.ArtifactRoot, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(hash, version.PackageHash, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add("Generated strategy package hash does not match manifest hash.");
            }
        }

        if (errors.Count == 0)
        {
            var syntax = await RunPythonAsync(
                _options.CodeGen.PythonExecutable,
                new[] { "-B", "-m", "py_compile", "strategy.py", "tests/test_strategy.py" },
                version.ArtifactRoot,
                cancellationToken).ConfigureAwait(false);
            if (!syntax.Success)
            {
                errors.Add($"Python static validation failed: {syntax.Output}");
            }
        }

        if (errors.Count == 0)
        {
            var safety = await RunPythonAsync(
                _options.CodeGen.PythonExecutable,
                new[] { "-I", "-B", "-c", PythonSafetyScanScript(), "strategy.py", "tests/test_strategy.py" },
                version.ArtifactRoot,
                cancellationToken).ConfigureAwait(false);
            if (!safety.Success)
            {
                errors.Add($"Python safety validation failed: {safety.Output}");
            }
        }

        return BuildGateResult(version, PromotionGateStage.StaticValidation, errors);
    }

    private async Task<GeneratedStrategyGateValidationResult> ValidateUnitTestGateAsync(
        GeneratedStrategyVersion version,
        CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        var result = await RunPythonAsync(
            _options.CodeGen.PythonExecutable,
            new[] { "-B", "-m", "unittest", "discover", "-s", "tests", "-p", "test*.py", "-v" },
            version.ArtifactRoot,
            cancellationToken).ConfigureAwait(false);
        if (!result.Success)
        {
            errors.Add($"Generated strategy unit tests failed: {result.Output}");
        }

        return BuildGateResult(version, PromotionGateStage.UnitTest, errors);
    }

    private async Task<GeneratedStrategyGateValidationResult> ValidateReplayGateAsync(
        GeneratedStrategyVersion version,
        CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        var result = await RunPythonAsync(
            _options.CodeGen.PythonExecutable,
            new[] { "-B", "-c", ReplayValidationScript() },
            version.ArtifactRoot,
            cancellationToken).ConfigureAwait(false);
        if (!result.Success)
        {
            errors.Add($"Generated strategy replay validation failed: {result.Output}");
        }

        return BuildGateResult(version, PromotionGateStage.Replay, errors);
    }

    private static GeneratedStrategyValidationResult BuildValidationResult(
        GeneratedStrategyVersion version,
        IReadOnlyList<GeneratedStrategyGateValidationResult> gates)
    {
        var errors = gates.SelectMany(gate => gate.Errors).ToList();
        var evidenceJson = JsonSerializer.Serialize(new
        {
            version.StrategyId,
            version.Version,
            version.PackageHash,
            gates = gates.Select(gate => new
            {
                gate.Stage,
                gate.Passed,
                gate.Errors,
                gate.EvidenceJson
            }),
            errors
        }, JsonOptions);

        return new GeneratedStrategyValidationResult(errors.Count == 0, errors, evidenceJson, gates);
    }

    private static GeneratedStrategyGateValidationResult BuildGateResult(
        GeneratedStrategyVersion version,
        PromotionGateStage stage,
        IReadOnlyList<string> errors)
    {
        var evidenceJson = JsonSerializer.Serialize(new
        {
            version.StrategyId,
            version.Version,
            stage,
            passed = errors.Count == 0,
            errors
        }, JsonOptions);

        return new GeneratedStrategyGateValidationResult(stage, errors.Count == 0, errors, evidenceJson);
    }

    private string ResolveArtifactRoot()
    {
        return Path.GetFullPath(_options.ArtifactRoot);
    }

    private static string BuildManifestJson(GeneratedStrategySpec spec, string version, string packageHash)
    {
        JsonObject manifest;
        if (string.IsNullOrWhiteSpace(spec.ManifestJson))
        {
            manifest = new JsonObject();
        }
        else
        {
            var node = JsonNode.Parse(spec.ManifestJson)
                ?? throw new JsonException("Generated strategy manifestJson is empty.");
            manifest = node as JsonObject
                ?? throw new JsonException("Generated strategy manifestJson must be a JSON object.");
        }

        manifest["strategyId"] = spec.StrategyId;
        manifest["name"] = string.IsNullOrWhiteSpace(spec.Name) ? spec.StrategyId : spec.Name;
        manifest["version"] = version;
        manifest["entryPoint"] = "strategy.py:evaluate";
        manifest["packageHash"] = packageHash;
        manifest["parameterSchemaPath"] = "params.schema.json";
        manifest["replaySpecPath"] = "replay.json";
        manifest["riskEnvelopePath"] = "risk_envelope.json";
        manifest["enabled"] = true;
        manifest["configVersion"] = version;

        return manifest.ToJsonString(JsonOptions);
    }

    private static string NormalizeJson(string json, string fallback)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return fallback;
        }

        using var document = JsonDocument.Parse(json);
        return JsonSerializer.Serialize(document.RootElement, JsonOptions);
    }

    private static IReadOnlyList<string> ValidateJsonFile(string artifactRoot, string relativePath)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(artifactRoot, relativePath)));
            _ = document.RootElement.ValueKind;
            return Array.Empty<string>();
        }
        catch (JsonException ex)
        {
            return new[] { $"{relativePath} is not valid JSON: {ex.Message}" };
        }
    }

    private IReadOnlyList<string> ValidateRiskEnvelope(GeneratedStrategyVersion version)
    {
        var errors = new List<string>();
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(version.ArtifactRoot, "risk_envelope.json")));
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            return new[] { "risk_envelope.json must be a JSON object." };
        }

        var maxSingle = GetRequiredPositiveDecimal(document.RootElement, "maxSingleOrderNotional", "max_single_order_notional", errors);
        var maxCycle = GetRequiredPositiveDecimal(document.RootElement, "maxCycleNotional", "max_cycle_notional", errors);
        var maxTotal = GetRequiredPositiveDecimal(document.RootElement, "maxTotalNotional", "max_total_notional", errors);

        if (maxSingle is not null && maxSingle > _options.Canary.MaxSingleOrderNotional)
        {
            errors.Add($"risk_envelope.json maxSingleOrderNotional {maxSingle} exceeds configured cap {_options.Canary.MaxSingleOrderNotional}.");
        }

        if (maxCycle is not null && maxCycle > _options.Canary.MaxCycleNotional)
        {
            errors.Add($"risk_envelope.json maxCycleNotional {maxCycle} exceeds configured cap {_options.Canary.MaxCycleNotional}.");
        }

        if (maxTotal is not null && maxTotal > _options.Canary.MaxTotalNotional)
        {
            errors.Add($"risk_envelope.json maxTotalNotional {maxTotal} exceeds configured cap {_options.Canary.MaxTotalNotional}.");
        }

        if (maxSingle is not null && maxCycle is not null && maxCycle < maxSingle)
        {
            errors.Add("risk_envelope.json maxCycleNotional cannot be less than maxSingleOrderNotional.");
        }

        if (maxCycle is not null && maxTotal is not null && maxTotal < maxCycle)
        {
            errors.Add("risk_envelope.json maxTotalNotional cannot be less than maxCycleNotional.");
        }

        return errors;
    }

    private static decimal? GetRequiredPositiveDecimal(
        JsonElement root,
        string camelCaseName,
        string snakeCaseName,
        ICollection<string> errors)
    {
        if (!TryGetProperty(root, camelCaseName, snakeCaseName, out var value))
        {
            errors.Add($"risk_envelope.json must define {camelCaseName}.");
            return null;
        }

        decimal parsed;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out parsed))
        {
            if (parsed > 0)
            {
                return parsed;
            }
        }
        else if (value.ValueKind == JsonValueKind.String
                 && decimal.TryParse(value.GetString(), out parsed)
                 && parsed > 0)
        {
            return parsed;
        }

        errors.Add($"risk_envelope.json {camelCaseName} must be a positive decimal.");
        return null;
    }

    private static bool TryGetProperty(
        JsonElement root,
        string camelCaseName,
        string snakeCaseName,
        out JsonElement value)
    {
        return root.TryGetProperty(camelCaseName, out value)
            || root.TryGetProperty(snakeCaseName, out value);
    }

    private static async Task<string> ComputePackageHashAsync(
        string artifactRoot,
        CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();
        foreach (var file in Directory.EnumerateFiles(artifactRoot, "*", SearchOption.AllDirectories)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var relative = Path.GetRelativePath(artifactRoot, file).Replace('\\', '/');
            if (string.Equals(relative, "manifest.json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var bytes = await File.ReadAllBytesAsync(file, cancellationToken).ConfigureAwait(false);
            builder.Append(relative).Append(':').Append(Convert.ToHexString(SHA256.HashData(bytes))).AppendLine();
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()))).ToLowerInvariant();
    }

    private static string SanitizePathSegment(string value)
    {
        var chars = value
            .Select(ch => char.IsLetterOrDigit(ch) || ch is '_' or '-' ? ch : '_')
            .ToArray();
        return new string(chars).Trim('_');
    }

    private static async Task<(bool Success, string Output)> RunPythonAsync(
        string executable,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = workingDirectory,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        ApplySafePythonEnvironment(process.StartInfo);

        process.Start();
        var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        var stderr = await process.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return (process.ExitCode == 0, stdout + stderr);
    }

    private static void ApplySafePythonEnvironment(ProcessStartInfo startInfo)
    {
        var original = Environment.GetEnvironmentVariables();
        startInfo.Environment.Clear();
        foreach (var name in SafeEnvironmentVariableNames)
        {
            if (original.Contains(name) && original[name] is string value && !string.IsNullOrWhiteSpace(value))
            {
                startInfo.Environment[name] = value;
            }
        }

        startInfo.Environment["PYTHONIOENCODING"] = "utf-8";
        startInfo.Environment["PYTHONDONTWRITEBYTECODE"] = "1";
        startInfo.Environment["PYTHONNOUSERSITE"] = "1";
        startInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        startInfo.Environment["DOTNET_NOLOGO"] = "1";
    }

    private static string PythonSafetyScanScript()
    {
        return """
import ast
import sys

FORBIDDEN_IMPORT_ROOTS = {
    "asyncio", "builtins", "ctypes", "ftplib", "glob", "http", "importlib",
    "marshal", "multiprocessing", "os", "pathlib", "pickle", "requests",
    "shutil", "socket", "ssl", "subprocess", "sys", "tempfile", "threading",
    "urllib"
}
FORBIDDEN_CALLS = {
    "__import__", "breakpoint", "compile", "delattr", "dir", "eval", "exec",
    "globals", "help", "input", "locals", "open", "setattr", "vars"
}
FORBIDDEN_TEXT = {
    "api_key", "apikey", "private_key", "privatekey", "secret", "passphrase"
}

errors = []
for path in sys.argv[1:]:
    with open(path, "r", encoding="utf-8") as handle:
        source = handle.read()
    lowered = source.lower()
    for token in FORBIDDEN_TEXT:
        if token in lowered:
            errors.append(f"{path}: forbidden secret-related token {token}")
    tree = ast.parse(source, filename=path)
    for node in ast.walk(tree):
        if isinstance(node, ast.Import):
            for alias in node.names:
                root = alias.name.split(".", 1)[0]
                if root in FORBIDDEN_IMPORT_ROOTS:
                    errors.append(f"{path}: forbidden import {alias.name}")
        elif isinstance(node, ast.ImportFrom):
            root = (node.module or "").split(".", 1)[0]
            if root in FORBIDDEN_IMPORT_ROOTS:
                errors.append(f"{path}: forbidden import {node.module}")
        elif isinstance(node, ast.Call) and isinstance(node.func, ast.Name):
            if node.func.id in FORBIDDEN_CALLS:
                errors.append(f"{path}: forbidden call {node.func.id}")

if errors:
    print("\n".join(errors))
    raise SystemExit(1)
""";
    }

    private static string ReplayValidationScript()
    {
        return """
import copy
import importlib.util
import json
from decimal import Decimal, InvalidOperation

def load_json(path):
    with open(path, "r", encoding="utf-8") as handle:
        return json.load(handle)

def normalize(value):
    return json.dumps(value, sort_keys=True, separators=(",", ":"), ensure_ascii=False)

def partial_match(expected, actual, path="$"):
    if isinstance(expected, dict):
        if not isinstance(actual, dict):
            raise AssertionError(f"{path}: expected object")
        for key, value in expected.items():
            if key not in actual:
                raise AssertionError(f"{path}.{key}: missing")
            partial_match(value, actual[key], f"{path}.{key}")
    elif isinstance(expected, list):
        if not isinstance(actual, list):
            raise AssertionError(f"{path}: expected array")
        if len(actual) != len(expected):
            raise AssertionError(f"{path}: expected length {len(expected)}, got {len(actual)}")
        for index, value in enumerate(expected):
            partial_match(value, actual[index], f"{path}[{index}]")
    elif actual != expected:
        raise AssertionError(f"{path}: expected {expected!r}, got {actual!r}")

def get_decimal(value, name):
    try:
        return Decimal(str(value))
    except (InvalidOperation, TypeError):
        raise AssertionError(f"intent {name} must be decimal-compatible")

def require_response_contract(response):
    if not isinstance(response, dict):
        raise AssertionError("response must be an object")
    action = response.get("action")
    if action not in {"enter", "exit", "skip"}:
        raise AssertionError("response action must be enter, exit, or skip")
    for field in ("reasonCode", "reason"):
        if not isinstance(response.get(field), str) or not response[field]:
            raise AssertionError(f"response {field} must be a non-empty string")
    if not isinstance(response.get("intents"), list):
        raise AssertionError("response intents must be an array")
    if not isinstance(response.get("telemetry"), dict):
        raise AssertionError("response telemetry must be an object")
    if not isinstance(response.get("statePatch"), dict):
        raise AssertionError("response statePatch must be an object")

def require_risk_envelope(response, envelope):
    max_single = Decimal(str(risk_value(envelope, "maxSingleOrderNotional", "max_single_order_notional")))
    max_cycle = Decimal(str(risk_value(envelope, "maxCycleNotional", "max_cycle_notional")))
    cycle = Decimal("0")
    for intent in response.get("intents", []):
        if not isinstance(intent, dict):
            raise AssertionError("intent must be an object")
        price = get_decimal(intent.get("price"), "price")
        quantity = get_decimal(intent.get("quantity"), "quantity")
        notional = abs(price * quantity)
        if notional > max_single:
            raise AssertionError(f"intent notional {notional} exceeds maxSingleOrderNotional {max_single}")
        cycle += notional
    if cycle > max_cycle:
        raise AssertionError(f"cycle notional {cycle} exceeds maxCycleNotional {max_cycle}")

def risk_value(envelope, camel_name, snake_name):
    if camel_name in envelope:
        return envelope[camel_name]
    if snake_name in envelope:
        return envelope[snake_name]
    raise AssertionError(f"risk envelope missing {camel_name}")

spec = importlib.util.spec_from_file_location("generated_strategy", "strategy.py")
if spec is None or spec.loader is None:
    raise RuntimeError("failed to load strategy.py")
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)
if not hasattr(module, "evaluate"):
    raise RuntimeError("strategy.py must expose evaluate(input)")

replay = load_json("replay.json")
envelope = load_json("risk_envelope.json")
cases = replay.get("cases")
if not isinstance(cases, list) or not cases:
    raise AssertionError("replay.json must contain a non-empty cases array")

for index, case in enumerate(cases):
    if not isinstance(case, dict):
        raise AssertionError(f"case {index} must be an object")
    payload = case.get("input", case.get("request"))
    if not isinstance(payload, dict):
        raise AssertionError(f"case {index} must define object input")
    first = module.evaluate(copy.deepcopy(payload))
    second = module.evaluate(copy.deepcopy(payload))
    require_response_contract(first)
    require_response_contract(second)
    require_risk_envelope(first, envelope)
    if normalize(first) != normalize(second):
        raise AssertionError(f"case {index} is non-deterministic")
    expected = case.get("expected", case.get("expect"))
    if expected is not None:
        partial_match(expected, first, f"case {index}.expected")

print(json.dumps({"cases": len(cases)}, separators=(",", ":")))
""";
    }
}
