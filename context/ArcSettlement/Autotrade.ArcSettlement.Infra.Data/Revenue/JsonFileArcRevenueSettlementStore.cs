using System.Text.Json;
using Autotrade.ArcSettlement.Application.Contract.Configuration;
using Autotrade.ArcSettlement.Application.Contract.Proofs;
using Autotrade.ArcSettlement.Application.Contract.Revenue;
using Autotrade.ArcSettlement.Application.Revenue;
using Microsoft.Extensions.Options;

namespace Autotrade.ArcSettlement.Infra.Data.Revenue;

public sealed class JsonFileArcRevenueSettlementStore(
    IOptionsMonitor<ArcSettlementOptions> options) : IArcRevenueSettlementStore
{
    private readonly SemaphoreSlim _lock = new(1, 1);

    public async Task<ArcRevenueSettlementRecord?> GetBySettlementIdAsync(
        string settlementId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settlementId);

        var records = await ReadAllAsync(cancellationToken).ConfigureAwait(false);
        return records.FirstOrDefault(record => IsSame(record.SettlementId, settlementId));
    }

    public async Task<IReadOnlyList<ArcRevenueSettlementRecord>> ListAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        var records = await ReadAllAsync(cancellationToken).ConfigureAwait(false);
        return records
            .OrderByDescending(record => record.RecordedAtUtc)
            .Take(Math.Max(1, limit))
            .ToArray();
    }

    public async Task UpsertAsync(
        ArcRevenueSettlementRecord record,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var records = await ReadAllUnlockedAsync(cancellationToken).ConfigureAwait(false);
            var index = records.FindIndex(item => IsSame(item.SettlementId, record.SettlementId));
            if (index >= 0)
            {
                records[index] = record;
            }
            else
            {
                records.Add(record);
            }

            var path = ResolveStorePath();
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
            var json = JsonSerializer.Serialize(records, ArcProofJson.StableSerializerOptions);
            await File.WriteAllTextAsync(path, json, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<List<ArcRevenueSettlementRecord>> ReadAllAsync(CancellationToken cancellationToken)
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

    private async Task<List<ArcRevenueSettlementRecord>> ReadAllUnlockedAsync(CancellationToken cancellationToken)
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

        return JsonSerializer.Deserialize<List<ArcRevenueSettlementRecord>>(
                json,
                ArcProofJson.StableSerializerOptions)
            ?? [];
    }

    private string ResolveStorePath()
    {
        var configuredPath = options.CurrentValue.RevenueSettlementStorePath;
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            configuredPath = "artifacts/arc-settlement/revenue-settlements.json";
        }

        return Path.GetFullPath(configuredPath);
    }

    private static bool IsSame(string left, string right)
        => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
