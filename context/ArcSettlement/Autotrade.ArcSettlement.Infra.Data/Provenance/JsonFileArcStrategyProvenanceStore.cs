using System.Text.Json;
using Autotrade.ArcSettlement.Application.Contract.Configuration;
using Autotrade.ArcSettlement.Application.Contract.Proofs;
using Autotrade.ArcSettlement.Application.Contract.Provenance;
using Autotrade.ArcSettlement.Application.Provenance;
using Microsoft.Extensions.Options;

namespace Autotrade.ArcSettlement.Infra.Data.Provenance;

public sealed class JsonFileArcStrategyProvenanceStore(
    IOptionsMonitor<ArcSettlementOptions> options) : IArcStrategyProvenanceStore
{
    private readonly SemaphoreSlim _lock = new(1, 1);

    public async Task<ArcStrategyProvenanceRecord?> GetByHashAsync(
        string provenanceHash,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provenanceHash);

        var records = await ReadAllAsync(cancellationToken).ConfigureAwait(false);
        return records.FirstOrDefault(record => IsSame(record.ProvenanceHash, provenanceHash));
    }

    public async Task<ArcStrategyProvenanceRecord?> GetBySourceAsync(
        ArcProvenanceSourceModule sourceModule,
        string sourceId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);

        var records = await ReadAllAsync(cancellationToken).ConfigureAwait(false);
        return records.FirstOrDefault(record =>
            record.Document.SourceModule == sourceModule && IsSame(record.Document.SourceId, sourceId));
    }

    public async Task<IReadOnlyList<ArcStrategyProvenanceRecord>> ListAsync(
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
        ArcStrategyProvenanceRecord record,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var records = await ReadAllUnlockedAsync(cancellationToken).ConfigureAwait(false);
            var index = records.FindIndex(item => IsSame(item.ProvenanceHash, record.ProvenanceHash));
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

    private async Task<List<ArcStrategyProvenanceRecord>> ReadAllAsync(CancellationToken cancellationToken)
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

    private async Task<List<ArcStrategyProvenanceRecord>> ReadAllUnlockedAsync(CancellationToken cancellationToken)
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

        return JsonSerializer.Deserialize<List<ArcStrategyProvenanceRecord>>(
                json,
                ArcProofJson.StableSerializerOptions)
            ?? [];
    }

    private string ResolveStorePath()
    {
        var configuredPath = options.CurrentValue.ProvenanceStorePath;
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            configuredPath = "artifacts/arc-settlement/provenance.json";
        }

        return Path.GetFullPath(configuredPath);
    }

    private static bool IsSame(string left, string right)
        => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
