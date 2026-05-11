using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Autotrade.SelfImprove.Application.Python;

public sealed class OutOfProcessPythonStrategyRuntime : IPythonStrategyRuntime
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        PropertyNameCaseInsensitive = true
    };

    private readonly SelfImproveOptions _options;

    public OutOfProcessPythonStrategyRuntime(IOptions<SelfImproveOptions> options)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task<PythonStrategyResponse> EvaluateAsync(
        PythonStrategyManifest manifest,
        PythonStrategyRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(request);

        var workerAssemblyPath = ResolveWorkerAssemblyPath();
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(_options.CodeGen.WorkerTimeoutSeconds, 1, 60)));
        var pythonDllPath = await ResolvePythonDllPathAsync(timeoutCts.Token).ConfigureAwait(false);

        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = _options.CodeGen.DotnetExecutable,
            WorkingDirectory = manifest.ArtifactRoot,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        process.StartInfo.ArgumentList.Add(workerAssemblyPath);
        process.StartInfo.Environment.Clear();
        process.StartInfo.Environment["PYTHONIOENCODING"] = "utf-8";
        process.StartInfo.Environment["PYTHONDONTWRITEBYTECODE"] = "1";
        process.StartInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        process.StartInfo.Environment["DOTNET_NOLOGO"] = "1";
        if (!string.IsNullOrWhiteSpace(pythonDllPath))
        {
            process.StartInfo.Environment["PYTHONNET_PYDLL"] = pythonDllPath;
            process.StartInfo.Environment["PYTHONHOME"] = Path.GetDirectoryName(pythonDllPath) ?? string.Empty;
        }

        process.Start();

        var payload = JsonSerializer.Serialize(
            new PythonWorkerRequest(
                Path.Combine(manifest.ArtifactRoot, "strategy.py"),
                JsonSerializer.Serialize(request, JsonOptions)),
            JsonOptions);
        await process.StandardInput.WriteAsync(payload.AsMemory(), timeoutCts.Token).ConfigureAwait(false);
        process.StandardInput.Close();

        var stdoutTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
        var stderrTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);
        await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            throw new PythonStrategyRuntimeException($"Python worker failed: {Trim(stderr)}");
        }

        try
        {
            var response = JsonSerializer.Deserialize<PythonStrategyResponse>(stdout, JsonOptions);
            return response ?? throw new PythonStrategyRuntimeException("Python worker returned empty response.");
        }
        catch (JsonException ex)
        {
            throw new PythonStrategyRuntimeException($"Python worker returned invalid JSON: {Trim(stdout)}", ex);
        }
    }

    private static string Trim(string value)
    {
        return value.Length <= 1000 ? value : value[..1000];
    }

    private async Task<string?> ResolvePythonDllPathAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(_options.CodeGen.PythonDllPath))
        {
            var configuredPath = Path.GetFullPath(_options.CodeGen.PythonDllPath);
            if (!File.Exists(configuredPath))
            {
                throw new FileNotFoundException(
                    "Configured SelfImprove:CodeGen:PythonDllPath was not found.",
                    configuredPath);
            }

            return configuredPath;
        }

        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = _options.CodeGen.PythonExecutable,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        process.StartInfo.ArgumentList.Add("-c");
        process.StartInfo.ArgumentList.Add(
            "import os, sys, sysconfig; lib=sysconfig.get_config_var('LDLIBRARY') or ''; print(os.path.join(sys.base_prefix, lib))");

        try
        {
            process.Start();
            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            var stdout = await stdoutTask.ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);
            var candidate = stdout.Trim();

            if (process.ExitCode == 0 && File.Exists(candidate))
            {
                return candidate;
            }

            if (!string.IsNullOrWhiteSpace(stderr))
            {
                throw new PythonStrategyRuntimeException(
                    $"Failed to resolve Python runtime DLL from SelfImprove:CodeGen:PythonExecutable: {Trim(stderr)}");
            }

            return null;
        }
        catch (Exception ex) when (ex is not PythonStrategyRuntimeException)
        {
            throw new PythonStrategyRuntimeException(
                "Failed to resolve Python runtime DLL from SelfImprove:CodeGen:PythonExecutable.",
                ex);
        }
    }

    private string ResolveWorkerAssemblyPath()
    {
        if (!string.IsNullOrWhiteSpace(_options.CodeGen.WorkerAssemblyPath))
        {
            return Path.GetFullPath(_options.CodeGen.WorkerAssemblyPath);
        }

        var local = Path.Combine(AppContext.BaseDirectory, "Autotrade.SelfImprove.PythonWorker.dll");
        if (File.Exists(local))
        {
            return local;
        }

        var repoRoot = FindRepositoryRoot(AppContext.BaseDirectory);
        if (repoRoot is not null)
        {
            foreach (var configuration in new[] { "Debug", "Release" })
            {
                var candidate = Path.Combine(
                    repoRoot,
                    "context",
                    "SelfImprove",
                    "Autotrade.SelfImprove.PythonWorker",
                    "bin",
                    configuration,
                    "net10.0",
                    "Autotrade.SelfImprove.PythonWorker.dll");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        throw new FileNotFoundException(
            "Autotrade.SelfImprove.PythonWorker.dll was not found. Build the solution or set SelfImprove:CodeGen:WorkerAssemblyPath.");
    }

    private static string? FindRepositoryRoot(string start)
    {
        var directory = new DirectoryInfo(Path.GetFullPath(start));
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Autotrade.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }
}

internal sealed record PythonWorkerRequest(
    string StrategyModulePath,
    string RequestJson);

public sealed class PythonStrategyRuntimeException : Exception
{
    public PythonStrategyRuntimeException(string message) : base(message)
    {
    }

    public PythonStrategyRuntimeException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
