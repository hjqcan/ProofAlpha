using System.Text.Json;
using Autotrade.ArcSettlement.Application.Contract.Configuration;
using Autotrade.ArcSettlement.Application.Contract.Proofs;
using Autotrade.ArcSettlement.Application.Contract.Signals;
using Autotrade.ArcSettlement.Application.Proofs;
using Autotrade.ArcSettlement.Application.Signals;
using Microsoft.Extensions.Options;

namespace Autotrade.ArcSettlement.Infra.Data.Signals;

public sealed class JsonFileArcSignalPublicationStore(
    IOptionsMonitor<ArcSettlementOptions> options) : IArcSignalPublicationStore
{
    private readonly SemaphoreSlim _lock = new(1, 1);

    public async Task<ArcSignalPublicationRecord?> GetBySignalIdAsync(
        string signalId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(signalId);

        var records = await ReadAllAsync(cancellationToken).ConfigureAwait(false);
        return records.FirstOrDefault(record => IsSame(record.SignalId, signalId));
    }

    public async Task<ArcSignalPublicationRecord?> GetBySourceAsync(
        ArcProofSourceKind sourceKind,
        string sourceId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);

        var records = await ReadAllAsync(cancellationToken).ConfigureAwait(false);
        return records.FirstOrDefault(record =>
            record.SourceKind == sourceKind && IsSame(record.SourceId, sourceId));
    }

    public async Task<IReadOnlyList<ArcSignalPublicationRecord>> ListAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        var records = await ReadAllAsync(cancellationToken).ConfigureAwait(false);
        return records
            .OrderByDescending(record => record.CreatedAtUtc)
            .Take(Math.Max(1, limit))
            .ToArray();
    }

    public async Task UpsertAsync(
        ArcSignalPublicationRecord record,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var records = await ReadAllUnlockedAsync(cancellationToken).ConfigureAwait(false);
            var index = records.FindIndex(item => IsSame(item.SignalId, record.SignalId));
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

    private async Task<List<ArcSignalPublicationRecord>> ReadAllAsync(CancellationToken cancellationToken)
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

    private async Task<List<ArcSignalPublicationRecord>> ReadAllUnlockedAsync(CancellationToken cancellationToken)
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

        return JsonSerializer.Deserialize<List<ArcSignalPublicationRecord>>(
                json,
                ArcProofJson.StableSerializerOptions)
            ?? [];
    }

    private string ResolveStorePath()
    {
        var configuredPath = options.CurrentValue.SignalPublicationStorePath;
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            configuredPath = "artifacts/arc-settlement/signals.json";
        }

        return Path.GetFullPath(configuredPath);
    }

    private static bool IsSame(string left, string right)
        => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
