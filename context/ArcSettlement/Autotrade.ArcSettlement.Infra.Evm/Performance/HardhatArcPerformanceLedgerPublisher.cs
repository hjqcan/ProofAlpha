using System.Globalization;
using System.Text.Json;
using Autotrade.ArcSettlement.Application.Contract.Configuration;
using Autotrade.ArcSettlement.Application.Performance;
using Autotrade.ArcSettlement.Domain.Shared;
using Autotrade.ArcSettlement.Infra.Evm.Hardhat;
using Microsoft.Extensions.Options;

namespace Autotrade.ArcSettlement.Infra.Evm.Performance;

public sealed class HardhatArcPerformanceLedgerPublisher(
    IOptionsMonitor<ArcSettlementOptions> options,
    IArcHardhatPerformanceLedgerProcessRunner processRunner) : IArcPerformanceLedgerPublisher
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ArcPerformanceLedgerPublishResult> PublishAsync(
        ArcPerformanceLedgerPublishPayload payload,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payload);

        var currentOptions = options.CurrentValue;
        var workspacePath = ResolveContractsWorkspacePath(currentOptions.EvmPublisher.ContractsWorkspacePath);
        var request = CreateRequest(currentOptions, payload);
        var processResult = await processRunner.RunAsync(
                new ArcHardhatPerformanceLedgerProcessRequest(
                    workspacePath,
                    ResolveNetworkName(currentOptions),
                    ResolveTimeoutSeconds(currentOptions),
                    JsonSerializer.Serialize(request, JsonOptions),
                    CreateEnvironment(currentOptions)),
                cancellationToken)
            .ConfigureAwait(false);

        var result = JsonSerializer.Deserialize<HardhatPerformanceOutcomeResult>(
            processResult.ResultJson,
            JsonOptions);
        if (result is null)
        {
            throw new InvalidOperationException("Hardhat performance ledger publisher returned an empty result.");
        }

        if (result.Duplicate)
        {
            throw new ArcPerformanceLedgerDuplicateException(
                $"PerformanceLedger rejected duplicate outcome for signal '{payload.SignalId}'.");
        }

        if (string.IsNullOrWhiteSpace(result.TransactionHash))
        {
            throw new InvalidOperationException("Hardhat performance ledger publisher did not return a transaction hash.");
        }

        return new ArcPerformanceLedgerPublishResult(result.TransactionHash, result.Confirmed);
    }

    private static HardhatPerformanceOutcomeRequest CreateRequest(
        ArcSettlementOptions options,
        ArcPerformanceLedgerPublishPayload payload)
        => new(
            options.ChainId,
            options.Contracts.PerformanceLedger,
            NormalizeBytes32(payload.SignalId, nameof(payload.SignalId)),
            (int)payload.Status,
            ToWholeNumberString(payload.RealizedPnlBps ?? 0m, nameof(payload.RealizedPnlBps)),
            ToWholeNumberString(payload.SlippageBps ?? 0m, nameof(payload.SlippageBps)),
            NormalizeBytes32(payload.OutcomeHash, nameof(payload.OutcomeHash)));

    private static IReadOnlyDictionary<string, string> CreateEnvironment(ArcSettlementOptions options)
    {
        var environment = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["LOCAL_RPC_URL"] = options.RpcUrl,
            ["ARC_TESTNET_RPC_URL"] = options.RpcUrl,
            ["ARC_TESTNET_CHAIN_ID"] = options.ChainId.ToString(CultureInfo.InvariantCulture)
        };

        var configuredSecretName = options.Wallet.PrivateKeyEnvironmentVariable;
        if (!string.IsNullOrWhiteSpace(configuredSecretName)
            && !string.Equals(
                configuredSecretName,
                ArcSettlementConstants.DefaultPrivateKeyEnvironmentVariable,
                StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(configuredSecretName)))
        {
            environment[ArcSettlementConstants.DefaultPrivateKeyEnvironmentVariable] =
                Environment.GetEnvironmentVariable(configuredSecretName)!;
        }

        return environment;
    }

    private static string ResolveNetworkName(ArcSettlementOptions options)
        => string.IsNullOrWhiteSpace(options.EvmPublisher.NetworkName)
            ? "localhost"
            : options.EvmPublisher.NetworkName;

    private static int ResolveTimeoutSeconds(ArcSettlementOptions options)
        => Math.Clamp(options.EvmPublisher.RequestTimeoutSeconds, 10, 600);

    private static string ResolveContractsWorkspacePath(string configuredPath)
    {
        var path = string.IsNullOrWhiteSpace(configuredPath)
            ? "interfaces/ArcContracts"
            : configuredPath;
        var checkedPaths = new List<string>();

        foreach (var candidate in BuildWorkspaceCandidates(path))
        {
            var fullPath = Path.GetFullPath(candidate);
            checkedPaths.Add(fullPath);
            if (IsArcContractsWorkspace(fullPath))
            {
                return fullPath;
            }
        }

        throw new DirectoryNotFoundException(
            $"Arc contracts workspace was not found. Checked: {string.Join(", ", checkedPaths.Distinct(StringComparer.OrdinalIgnoreCase))}");
    }

    private static IEnumerable<string> BuildWorkspaceCandidates(string configuredPath)
    {
        if (Path.IsPathRooted(configuredPath))
        {
            yield return configuredPath;
            yield break;
        }

        foreach (var startDirectory in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            yield return Path.Combine(startDirectory, configuredPath);

            var current = new DirectoryInfo(startDirectory);
            while (current is not null)
            {
                yield return Path.Combine(current.FullName, configuredPath);
                current = current.Parent;
            }
        }
    }

    private static bool IsArcContractsWorkspace(string path)
        => File.Exists(Path.Combine(path, "package.json"))
           && File.Exists(Path.Combine(path, "scripts", "record-outcome.cjs"));

    private static string NormalizeBytes32(string value, string fieldName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, fieldName);
        var normalized = value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? value.ToLowerInvariant()
            : $"0x{value.ToLowerInvariant()}";
        if (normalized.Length != 66 || !normalized[2..].All(Uri.IsHexDigit))
        {
            throw new ArgumentException($"{fieldName} must be a 32-byte hex value.", fieldName);
        }

        return normalized;
    }

    private static string ToWholeNumberString(decimal value, string fieldName)
    {
        if (decimal.Truncate(value) != value)
        {
            throw new ArgumentException($"{fieldName} must be an integer bps value.", fieldName);
        }

        return value.ToString("0", CultureInfo.InvariantCulture);
    }

    private sealed record HardhatPerformanceOutcomeRequest(
        long ChainId,
        string PerformanceLedger,
        string SignalId,
        int Status,
        string RealizedPnlBps,
        string SlippageBps,
        string OutcomeHash);

    private sealed record HardhatPerformanceOutcomeResult(
        string? TransactionHash,
        bool Confirmed,
        bool Duplicate,
        string? ErrorCode);
}

public sealed record ArcHardhatPerformanceLedgerProcessRequest(
    string ContractsWorkspacePath,
    string NetworkName,
    int TimeoutSeconds,
    string RequestJson,
    IReadOnlyDictionary<string, string> EnvironmentVariables);

public sealed record ArcHardhatPerformanceLedgerProcessResult(
    string ResultJson,
    string StandardOutput,
    string StandardError);

public interface IArcHardhatPerformanceLedgerProcessRunner
{
    Task<ArcHardhatPerformanceLedgerProcessResult> RunAsync(
        ArcHardhatPerformanceLedgerProcessRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class HardhatPerformanceLedgerProcessRunner : IArcHardhatPerformanceLedgerProcessRunner
{
    public async Task<ArcHardhatPerformanceLedgerProcessResult> RunAsync(
        ArcHardhatPerformanceLedgerProcessRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await HardhatScriptProcess.RunAsync(
                new HardhatScriptProcessRequest(
                    request.ContractsWorkspacePath,
                    request.NetworkName,
                    request.TimeoutSeconds,
                    "scripts/record-outcome.cjs",
                    "ARC_OUTCOME_RECORD_REQUEST",
                    "ARC_OUTCOME_RECORD_RESULT",
                    request.RequestJson,
                    request.EnvironmentVariables,
                    "proofalpha-arc-outcome"),
                cancellationToken)
            .ConfigureAwait(false);
        return new ArcHardhatPerformanceLedgerProcessResult(
            result.ResultJson,
            result.StandardOutput,
            result.StandardError);
    }
}
