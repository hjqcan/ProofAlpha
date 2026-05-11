using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Autotrade.SelfImprove.Application.Contract.GeneratedStrategies;
using Autotrade.SelfImprove.Application.Contract.Proposals;
using Autotrade.SelfImprove.Domain.Entities;
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

        var version = $"v{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
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
        var manifest = new GeneratedStrategyManifest(
            spec.StrategyId,
            spec.Name,
            version,
            "strategy.py:evaluate",
            packageHash,
            "params.schema.json",
            "replay.json",
            "risk_envelope.json",
            Enabled: true,
            ConfigVersion: version);
        var manifestJson = JsonSerializer.Serialize(manifest, JsonOptions);
        await File.WriteAllTextAsync(Path.Combine(stagingRoot, "manifest.json"), manifestJson, cancellationToken)
            .ConfigureAwait(false);
        packageHash = await ComputePackageHashAsync(stagingRoot, cancellationToken).ConfigureAwait(false);
        manifest = manifest with { PackageHash = packageHash };
        manifestJson = JsonSerializer.Serialize(manifest, JsonOptions);
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
        var errors = new List<string>();
        var required = new[]
        {
            "manifest.json",
            "strategy.py",
            "params.schema.json",
            "tests/test_strategy.py",
            "replay.json",
            "risk_envelope.json"
        };

        foreach (var relative in required)
        {
            if (!File.Exists(Path.Combine(version.ArtifactRoot, relative)))
            {
                errors.Add($"Missing generated strategy file: {relative}");
            }
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
                $"-m py_compile \"{Path.Combine(version.ArtifactRoot, "strategy.py")}\"",
                version.ArtifactRoot,
                cancellationToken).ConfigureAwait(false);
            if (!syntax.Success)
            {
                errors.Add($"Python static validation failed: {syntax.Output}");
            }
        }

        var evidenceJson = JsonSerializer.Serialize(new
        {
            version.StrategyId,
            version.Version,
            version.PackageHash,
            errors
        }, JsonOptions);

        return new GeneratedStrategyValidationResult(errors.Count == 0, errors, evidenceJson);
    }

    private string ResolveArtifactRoot()
    {
        return Path.GetFullPath(_options.ArtifactRoot);
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
        string arguments,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = executable,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        process.Start();
        var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        var stderr = await process.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return (process.ExitCode == 0, stdout + stderr);
    }
}
