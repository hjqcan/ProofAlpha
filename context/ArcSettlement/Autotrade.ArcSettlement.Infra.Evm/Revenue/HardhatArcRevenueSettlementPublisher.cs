using System.Globalization;
using System.Text.Json;
using Autotrade.ArcSettlement.Application.Contract.Configuration;
using Autotrade.ArcSettlement.Application.Revenue;
using Autotrade.ArcSettlement.Domain.Shared;
using Autotrade.ArcSettlement.Infra.Evm.Hardhat;
using Microsoft.Extensions.Options;

namespace Autotrade.ArcSettlement.Infra.Evm.Revenue;

public sealed class HardhatArcRevenueSettlementPublisher(
    IOptionsMonitor<ArcSettlementOptions> options,
    IArcHardhatRevenueSettlementProcessRunner processRunner) : IArcRevenueSettlementPublisher
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ArcRevenueSettlementPublishResult> PublishAsync(
        ArcRevenueSettlementPublishPayload payload,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payload);

        var currentOptions = options.CurrentValue;
        var workspacePath = ResolveContractsWorkspacePath(currentOptions.EvmPublisher.ContractsWorkspacePath);
        var request = CreateRequest(currentOptions, payload);
        var processResult = await processRunner.RunAsync(
                new ArcHardhatRevenueSettlementProcessRequest(
                    workspacePath,
                    ResolveNetworkName(currentOptions),
                    ResolveTimeoutSeconds(currentOptions),
                    JsonSerializer.Serialize(request, JsonOptions),
                    CreateEnvironment(currentOptions)),
                cancellationToken)
            .ConfigureAwait(false);

        var result = JsonSerializer.Deserialize<HardhatRevenueSettlementResult>(
            processResult.ResultJson,
            JsonOptions);
        if (result is null)
        {
            throw new InvalidOperationException("Hardhat revenue settlement publisher returned an empty result.");
        }

        if (result.Duplicate)
        {
            throw new ArcRevenueSettlementDuplicateException(
                $"RevenueSettlement rejected duplicate settlement '{payload.SettlementId}'.");
        }

        if (string.IsNullOrWhiteSpace(result.TransactionHash))
        {
            throw new InvalidOperationException("Hardhat revenue settlement publisher did not return a transaction hash.");
        }

        return new ArcRevenueSettlementPublishResult(result.TransactionHash, result.Confirmed);
    }

    private static HardhatRevenueSettlementRequest CreateRequest(
        ArcSettlementOptions options,
        ArcRevenueSettlementPublishPayload payload)
        => new(
            options.ChainId,
            options.Contracts.RevenueSettlement,
            NormalizeBytes32(payload.SettlementId, nameof(payload.SettlementId)),
            NormalizeBytes32(payload.SignalId, nameof(payload.SignalId)),
            NormalizeAddress(payload.TokenAddress, nameof(payload.TokenAddress)),
            RequireWholeNumberString(payload.GrossAmountMicroUsdc, nameof(payload.GrossAmountMicroUsdc)),
            payload.Recipients.Select((recipient, index) => NormalizeAddress(recipient, $"{nameof(payload.Recipients)}[{index}]")).ToArray(),
            ValidateShares(payload.ShareBps));

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
           && File.Exists(Path.Combine(path, "scripts", "record-settlement.cjs"));

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
        var normalized = value.Trim().ToLowerInvariant();
        if (normalized.Length != 42 || !normalized.StartsWith("0x", StringComparison.Ordinal) || !normalized[2..].All(Uri.IsHexDigit))
        {
            throw new ArgumentException($"{fieldName} must be an EVM address.", fieldName);
        }

        if (normalized[2..].All(character => character == '0'))
        {
            throw new ArgumentException($"{fieldName} cannot be the zero address.", fieldName);
        }

        return normalized;
    }

    private static string RequireWholeNumberString(string value, string fieldName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, fieldName);
        if (!value.All(char.IsDigit))
        {
            throw new ArgumentException($"{fieldName} must be an unsigned integer string.", fieldName);
        }

        return value;
    }

    private static IReadOnlyList<int> ValidateShares(IReadOnlyList<int> shares)
    {
        if (shares.Count == 0)
        {
            throw new ArgumentException("ShareBps must contain at least one share.", nameof(shares));
        }

        if (shares.Any(share => share <= 0) || shares.Sum() != 10_000)
        {
            throw new ArgumentException("ShareBps must be positive and sum to 10000.", nameof(shares));
        }

        return shares.ToArray();
    }

    private sealed record HardhatRevenueSettlementRequest(
        long ChainId,
        string RevenueSettlement,
        string SettlementId,
        string SignalId,
        string TokenAddress,
        string GrossAmountMicroUsdc,
        IReadOnlyList<string> Recipients,
        IReadOnlyList<int> ShareBps);

    private sealed record HardhatRevenueSettlementResult(
        string? TransactionHash,
        bool Confirmed,
        bool Duplicate,
        string? ErrorCode);
}

public sealed record ArcHardhatRevenueSettlementProcessRequest(
    string ContractsWorkspacePath,
    string NetworkName,
    int TimeoutSeconds,
    string RequestJson,
    IReadOnlyDictionary<string, string> EnvironmentVariables);

public sealed record ArcHardhatRevenueSettlementProcessResult(
    string ResultJson,
    string StandardOutput,
    string StandardError);

public interface IArcHardhatRevenueSettlementProcessRunner
{
    Task<ArcHardhatRevenueSettlementProcessResult> RunAsync(
        ArcHardhatRevenueSettlementProcessRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class HardhatRevenueSettlementProcessRunner : IArcHardhatRevenueSettlementProcessRunner
{
    public async Task<ArcHardhatRevenueSettlementProcessResult> RunAsync(
        ArcHardhatRevenueSettlementProcessRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await HardhatScriptProcess.RunAsync(
                new HardhatScriptProcessRequest(
                    request.ContractsWorkspacePath,
                    request.NetworkName,
                    request.TimeoutSeconds,
                    "scripts/record-settlement.cjs",
                    "ARC_REVENUE_SETTLEMENT_REQUEST",
                    "ARC_REVENUE_SETTLEMENT_RESULT",
                    request.RequestJson,
                    request.EnvironmentVariables,
                    "proofalpha-arc-revenue"),
                cancellationToken)
            .ConfigureAwait(false);
        return new ArcHardhatRevenueSettlementProcessResult(
            result.ResultJson,
            result.StandardOutput,
            result.StandardError);
    }
}
