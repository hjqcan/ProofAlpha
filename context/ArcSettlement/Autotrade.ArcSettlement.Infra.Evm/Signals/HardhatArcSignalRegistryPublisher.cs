using System.Globalization;
using System.Text.Json;
using Autotrade.ArcSettlement.Application.Contract.Configuration;
using Autotrade.ArcSettlement.Application.Signals;
using Autotrade.ArcSettlement.Domain.Shared;
using Autotrade.ArcSettlement.Infra.Evm.Hardhat;
using Microsoft.Extensions.Options;

namespace Autotrade.ArcSettlement.Infra.Evm.Signals;

public sealed class HardhatArcSignalRegistryPublisher(
    IOptionsMonitor<ArcSettlementOptions> options,
    IArcHardhatSignalPublisherProcessRunner processRunner) : IArcSignalRegistryPublisher
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ArcSignalRegistryPublishResult> PublishAsync(
        ArcSignalRegistryPublishPayload payload,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payload);

        var currentOptions = options.CurrentValue;
        var workspacePath = ResolveContractsWorkspacePath(currentOptions.EvmPublisher.ContractsWorkspacePath);
        var request = CreateRequest(currentOptions, payload);
        var processResult = await processRunner.RunAsync(
                new ArcHardhatSignalPublisherProcessRequest(
                    workspacePath,
                    ResolveNetworkName(currentOptions),
                    ResolveTimeoutSeconds(currentOptions),
                    JsonSerializer.Serialize(request, JsonOptions),
                    CreateEnvironment(currentOptions)),
                cancellationToken)
            .ConfigureAwait(false);

        var result = JsonSerializer.Deserialize<HardhatSignalPublicationResult>(
            processResult.ResultJson,
            JsonOptions);
        if (result is null)
        {
            throw new InvalidOperationException("Hardhat signal publisher returned an empty result.");
        }

        if (result.Duplicate)
        {
            throw new ArcSignalRegistryDuplicateException(
                $"SignalRegistry rejected duplicate signal '{payload.SignalId}'.");
        }

        if (string.IsNullOrWhiteSpace(result.TransactionHash))
        {
            throw new InvalidOperationException("Hardhat signal publisher did not return a transaction hash.");
        }

        return new ArcSignalRegistryPublishResult(result.TransactionHash, result.Confirmed);
    }

    private static HardhatSignalPublicationRequest CreateRequest(
        ArcSettlementOptions options,
        ArcSignalRegistryPublishPayload payload)
        => new(
            options.ChainId,
            options.Contracts.SignalRegistry,
            NormalizeBytes32(payload.SignalId, nameof(payload.SignalId)),
            NormalizeAddress(payload.AgentAddress, nameof(payload.AgentAddress)),
            payload.Venue,
            payload.StrategyKey,
            NormalizeBytes32(payload.ReasoningHash, nameof(payload.ReasoningHash)),
            NormalizeBytes32(payload.RiskEnvelopeHash, nameof(payload.RiskEnvelopeHash)),
            ToWholeNumberString(payload.ExpectedEdgeBps, nameof(payload.ExpectedEdgeBps)),
            ToUsdcAtomicString(payload.MaxNotionalUsdc, nameof(payload.MaxNotionalUsdc)),
            payload.ValidUntilUtc.ToUnixTimeSeconds());

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
           && File.Exists(Path.Combine(path, "scripts", "publish-signal.cjs"));

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

    private static string NormalizeAddress(string value, string fieldName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, fieldName);
        var normalized = value.ToLowerInvariant();
        if (normalized.Length != 42
            || !normalized.StartsWith("0x", StringComparison.Ordinal)
            || !normalized[2..].All(Uri.IsHexDigit)
            || normalized[2..].All(character => character == '0'))
        {
            throw new ArgumentException($"{fieldName} must be a non-zero EVM address.", fieldName);
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

    private static string ToUsdcAtomicString(decimal value, string fieldName)
    {
        if (value <= 0m)
        {
            throw new ArgumentException($"{fieldName} must be positive.", fieldName);
        }

        var scaled = value * 1_000_000m;
        if (decimal.Truncate(scaled) != scaled)
        {
            throw new ArgumentException($"{fieldName} must have at most 6 decimal places.", fieldName);
        }

        return scaled.ToString("0", CultureInfo.InvariantCulture);
    }

    private sealed record HardhatSignalPublicationRequest(
        long ChainId,
        string SignalRegistry,
        string SignalId,
        string AgentAddress,
        string Venue,
        string StrategyKey,
        string ReasoningHash,
        string RiskEnvelopeHash,
        string ExpectedEdgeBps,
        string MaxNotionalUsdcAtomic,
        long ValidUntilUnixSeconds);

    private sealed record HardhatSignalPublicationResult(
        string? TransactionHash,
        bool Confirmed,
        bool Duplicate,
        string? ErrorCode);
}

public sealed record ArcHardhatSignalPublisherProcessRequest(
    string ContractsWorkspacePath,
    string NetworkName,
    int TimeoutSeconds,
    string RequestJson,
    IReadOnlyDictionary<string, string> EnvironmentVariables);

public sealed record ArcHardhatSignalPublisherProcessResult(
    string ResultJson,
    string StandardOutput,
    string StandardError);

public interface IArcHardhatSignalPublisherProcessRunner
{
    Task<ArcHardhatSignalPublisherProcessResult> RunAsync(
        ArcHardhatSignalPublisherProcessRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class HardhatSignalPublisherProcessRunner : IArcHardhatSignalPublisherProcessRunner
{
    public async Task<ArcHardhatSignalPublisherProcessResult> RunAsync(
        ArcHardhatSignalPublisherProcessRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await HardhatScriptProcess.RunAsync(
                new HardhatScriptProcessRequest(
                    request.ContractsWorkspacePath,
                    request.NetworkName,
                    request.TimeoutSeconds,
                    "scripts/publish-signal.cjs",
                    "ARC_SIGNAL_PUBLISH_REQUEST",
                    "ARC_SIGNAL_PUBLISH_RESULT",
                    request.RequestJson,
                    request.EnvironmentVariables,
                    "proofalpha-arc-signal"),
                cancellationToken)
            .ConfigureAwait(false);
        return new ArcHardhatSignalPublisherProcessResult(
            result.ResultJson,
            result.StandardOutput,
            result.StandardError);
    }
}
