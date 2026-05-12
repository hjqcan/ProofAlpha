using System.Diagnostics;

namespace Autotrade.ArcSettlement.Infra.Evm.Hardhat;

internal sealed record HardhatScriptProcessRequest(
    string ContractsWorkspacePath,
    string NetworkName,
    int TimeoutSeconds,
    string ScriptPath,
    string RequestEnvironmentVariable,
    string ResultEnvironmentVariable,
    string RequestJson,
    IReadOnlyDictionary<string, string> EnvironmentVariables,
    string TempDirectoryName);

internal sealed record HardhatScriptProcessResult(
    string ResultJson,
    string StandardOutput,
    string StandardError);

internal static class HardhatScriptProcess
{
    public static async Task<HardhatScriptProcessResult> RunAsync(
        HardhatScriptProcessRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var tempDirectory = Path.Combine(
            Path.GetTempPath(),
            request.TempDirectoryName,
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        var requestPath = Path.Combine(tempDirectory, "request.json");
        var resultPath = Path.Combine(tempDirectory, "result.json");

        try
        {
            await File.WriteAllTextAsync(requestPath, request.RequestJson, cancellationToken)
                .ConfigureAwait(false);

            var startInfo = new ProcessStartInfo
            {
                FileName = OperatingSystem.IsWindows() ? "npx.cmd" : "npx",
                WorkingDirectory = request.ContractsWorkspacePath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            startInfo.ArgumentList.Add("hardhat");
            startInfo.ArgumentList.Add("run");
            startInfo.ArgumentList.Add(request.ScriptPath);
            startInfo.ArgumentList.Add("--network");
            startInfo.ArgumentList.Add(request.NetworkName);
            startInfo.Environment[request.RequestEnvironmentVariable] = requestPath;
            startInfo.Environment[request.ResultEnvironmentVariable] = resultPath;

            foreach (var (key, value) in request.EnvironmentVariables)
            {
                if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(value))
                {
                    startInfo.Environment[key] = value;
                }
            }

            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Failed to start Hardhat script process.");
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(request.TimeoutSeconds));

            try
            {
                await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                TryKill(process);
                throw new TimeoutException(
                    $"Hardhat script timed out after {request.TimeoutSeconds} seconds: {request.ScriptPath}.");
            }

            var standardOutput = await stdoutTask.ConfigureAwait(false);
            var standardError = await stderrTask.ConfigureAwait(false);
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"Hardhat script failed with exit code {process.ExitCode}. stderr: {TrimForError(standardError)} stdout: {TrimForError(standardOutput)}");
            }

            if (!File.Exists(resultPath))
            {
                throw new InvalidOperationException(
                    $"Hardhat script did not write a result file. stdout: {TrimForError(standardOutput)}");
            }

            var resultJson = await File.ReadAllTextAsync(resultPath, cancellationToken)
                .ConfigureAwait(false);
            return new HardhatScriptProcessResult(
                resultJson,
                standardOutput,
                standardError);
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static string TrimForError(string value)
        => value.Length <= 2000 ? value : value[..2000];
}
