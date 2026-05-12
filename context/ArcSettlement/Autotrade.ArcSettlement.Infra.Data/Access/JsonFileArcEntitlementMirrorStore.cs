using System.Text.Json;
using Autotrade.ArcSettlement.Application.Access;
using Autotrade.ArcSettlement.Application.Contract.Access;
using Autotrade.ArcSettlement.Application.Contract.Configuration;
using Autotrade.ArcSettlement.Application.Contract.Proofs;
using Microsoft.Extensions.Options;

namespace Autotrade.ArcSettlement.Infra.Data.Access;

public sealed class JsonFileArcEntitlementMirrorStore(
    IOptionsMonitor<ArcSettlementOptions> options) : IArcEntitlementMirrorStore
{
    private readonly SemaphoreSlim _lock = new(1, 1);

    public async Task<ArcSubscriptionMirror?> GetAsync(
        string walletAddress,
        string strategyKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(walletAddress);
        ArgumentException.ThrowIfNullOrWhiteSpace(strategyKey);

        var mirrors = await ReadAllAsync(cancellationToken).ConfigureAwait(false);
        return mirrors.FirstOrDefault(mirror =>
            IsSame(mirror.WalletAddress, walletAddress)
            && IsSame(mirror.StrategyKey, strategyKey));
    }

    public async Task<IReadOnlyList<ArcSubscriptionMirror>> ListAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        var mirrors = await ReadAllAsync(cancellationToken).ConfigureAwait(false);
        return mirrors
            .OrderByDescending(mirror => mirror.SyncedAtUtc)
            .Take(Math.Max(1, limit))
            .ToArray();
    }

    public async Task UpsertAsync(
        ArcSubscriptionMirror mirror,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mirror);

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var mirrors = await ReadAllUnlockedAsync(cancellationToken).ConfigureAwait(false);
            var index = mirrors.FindIndex(item =>
                IsSame(item.WalletAddress, mirror.WalletAddress)
                && IsSame(item.StrategyKey, mirror.StrategyKey));

            if (index >= 0)
            {
                mirrors[index] = mirror;
            }
            else
            {
                mirrors.Add(mirror);
            }

            var path = ResolveStorePath();
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
            var json = JsonSerializer.Serialize(mirrors, ArcProofJson.StableSerializerOptions);
            await File.WriteAllTextAsync(path, json, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<List<ArcSubscriptionMirror>> ReadAllAsync(CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await ReadAllUnlockedAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<List<ArcSubscriptionMirror>> ReadAllUnlockedAsync(CancellationToken cancellationToken)
    {
        var path = ResolveStorePath();
        if (!File.Exists(path))
        {
            return [];
        }

        var json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        return JsonSerializer.Deserialize<List<ArcSubscriptionMirror>>(
                json,
                ArcProofJson.StableSerializerOptions)
            ?? [];
    }

    private string ResolveStorePath()
    {
        var configuredPath = options.CurrentValue.EntitlementMirrorStorePath;
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            configuredPath = "artifacts/arc-settlement/entitlements.json";
        }

        return Path.GetFullPath(configuredPath);
    }

    private static bool IsSame(string left, string right)
        => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
