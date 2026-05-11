using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Autotrade.Strategy.Application.Audit;
using Autotrade.Strategy.Application.Persistence;
using Autotrade.Strategy.Application.Strategies.DualLeg;
using Autotrade.Strategy.Application.Strategies.Endgame;
using Autotrade.Strategy.Application.Strategies.LiquidityMaking;
using Autotrade.Strategy.Application.Strategies.LiquidityPulse;
using Autotrade.Strategy.Application.Strategies.RepricingLag;
using Autotrade.Strategy.Application.Strategies.Volatility;
using Autotrade.Strategy.Domain.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Autotrade.Strategy.Application.Parameters;

public sealed class StrategyParameterVersionService : IStrategyParameterVersionService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    private readonly IStrategyParameterVersionRepository _repository;
    private readonly IStrategyUnitOfWork _unitOfWork;
    private readonly ICommandAuditLogger _auditLogger;
    private readonly ILogger<StrategyParameterVersionService> _logger;
    private readonly IReadOnlyDictionary<string, StrategyOptionsAdapter> _adapters;

    public StrategyParameterVersionService(
        IStrategyParameterVersionRepository repository,
        IStrategyUnitOfWork unitOfWork,
        ICommandAuditLogger auditLogger,
        ILogger<StrategyParameterVersionService> logger,
        IOptionsMonitor<DualLegArbitrageOptions> dualLegOptions,
        IOptionsMonitorCache<DualLegArbitrageOptions> dualLegCache,
        IOptionsMonitor<EndgameSweepOptions> endgameOptions,
        IOptionsMonitorCache<EndgameSweepOptions> endgameCache,
        IOptionsMonitor<LiquidityPulseOptions> liquidityPulseOptions,
        IOptionsMonitorCache<LiquidityPulseOptions> liquidityPulseCache,
        IOptionsMonitor<LiquidityMakerOptions> liquidityMakerOptions,
        IOptionsMonitorCache<LiquidityMakerOptions> liquidityMakerCache,
        IOptionsMonitor<MicroVolatilityScalperOptions> microVolatilityOptions,
        IOptionsMonitorCache<MicroVolatilityScalperOptions> microVolatilityCache,
        IOptionsMonitor<RepricingLagArbitrageOptions> repricingLagOptions,
        IOptionsMonitorCache<RepricingLagArbitrageOptions> repricingLagCache)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _auditLogger = auditLogger ?? throw new ArgumentNullException(nameof(auditLogger));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _adapters = new Dictionary<string, StrategyOptionsAdapter>(StringComparer.OrdinalIgnoreCase)
        {
            ["dual_leg_arbitrage"] = CreateAdapter(
                "dual_leg_arbitrage",
                dualLegOptions,
                dualLegCache,
                options => options.Validate()),
            ["endgame_sweep"] = CreateAdapter(
                "endgame_sweep",
                endgameOptions,
                endgameCache,
                options => options.Validate()),
            ["liquidity_pulse"] = CreateAdapter(
                "liquidity_pulse",
                liquidityPulseOptions,
                liquidityPulseCache,
                options => options.Validate()),
            ["liquidity_maker"] = CreateAdapter(
                "liquidity_maker",
                liquidityMakerOptions,
                liquidityMakerCache,
                options => options.Validate()),
            ["micro_volatility_scalper"] = CreateAdapter(
                "micro_volatility_scalper",
                microVolatilityOptions,
                microVolatilityCache,
                options => options.Validate()),
            ["repricing_lag_arbitrage"] = CreateAdapter(
                "repricing_lag_arbitrage",
                repricingLagOptions,
                repricingLagCache,
                options => options.Validate())
        };
    }

    public async Task<StrategyParameterSnapshot> GetSnapshotAsync(
        string strategyId,
        int limit = 10,
        CancellationToken cancellationToken = default)
    {
        var adapter = ResolveAdapter(strategyId);
        var versions = await _repository
            .GetRecentAsync(adapter.StrategyId, Math.Clamp(limit, 1, 50), cancellationToken)
            .ConfigureAwait(false);

        return new StrategyParameterSnapshot(
            adapter.StrategyId,
            adapter.GetConfigVersion(),
            adapter.GetParameters(),
            versions.Select(ToRecord).ToArray());
    }

    public async Task<StrategyParameterMutationResult> UpdateAsync(
        string strategyId,
        StrategyParameterMutationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var adapter = ResolveAdapter(strategyId);
        var actor = NormalizeActor(request.Actor);
        var source = NormalizeSource(request.Source);
        var currentSnapshot = adapter.GetSnapshot();

        if (request.Changes.Count == 0)
        {
            return await RejectedAsync(
                adapter,
                "InvalidRequest",
                "At least one parameter change is required.",
                cancellationToken).ConfigureAwait(false);
        }

        StrategyOptionsSnapshot nextSnapshot;
        IReadOnlyList<StrategyParameterDiff> diff;
        try
        {
            (nextSnapshot, diff) = adapter.BuildUpdatedSnapshot(request.Changes);
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or OverflowException)
        {
            return await RejectedAsync(adapter, "InvalidRequest", exception.Message, cancellationToken)
                .ConfigureAwait(false);
        }

        if (diff.Count == 0)
        {
            return await RejectedAsync(
                adapter,
                "NoChanges",
                "Submitted values match the active strategy parameters.",
                cancellationToken).ConfigureAwait(false);
        }

        var version = await PersistAndApplyAsync(
            adapter,
            nextSnapshot,
            currentSnapshot,
            currentSnapshot.ConfigVersion,
            diff,
            "Update",
            source,
            actor,
            request.Reason,
            rollbackSourceVersionId: null,
            cancellationToken).ConfigureAwait(false);

        await LogAuditAsync(
            "control-room strategy parameters update",
            adapter.StrategyId,
            version,
            diff,
            source,
            actor,
            request.Reason,
            success: true,
            cancellationToken).ConfigureAwait(false);

        return new StrategyParameterMutationResult(
            true,
            "Accepted",
            $"Strategy {adapter.StrategyId} parameters updated to {version.ConfigVersion}.",
            ToRecord(version),
            await GetSnapshotAsync(adapter.StrategyId, cancellationToken: cancellationToken).ConfigureAwait(false));
    }

    public async Task<StrategyParameterMutationResult> RollbackAsync(
        string strategyId,
        StrategyParameterRollbackRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var adapter = ResolveAdapter(strategyId);
        var actor = NormalizeActor(request.Actor);
        var source = NormalizeSource(request.Source);
        var target = await _repository.GetAsync(request.VersionId, cancellationToken).ConfigureAwait(false);
        if (target is null || !string.Equals(target.StrategyId, adapter.StrategyId, StringComparison.OrdinalIgnoreCase))
        {
            return await RejectedAsync(
                adapter,
                "NotFound",
                "Rollback target version was not found for this strategy.",
                cancellationToken).ConfigureAwait(false);
        }

        StrategyOptionsSnapshot targetSnapshot;
        try
        {
            targetSnapshot = adapter.ParseSnapshot(target.SnapshotJson);
        }
        catch (JsonException exception)
        {
            return await RejectedAsync(
                adapter,
                "InvalidStoredVersion",
                $"Stored parameter version is not valid JSON: {exception.Message}",
                cancellationToken).ConfigureAwait(false);
        }

        var currentSnapshot = adapter.GetSnapshot();
        var diff = BuildDiff(currentSnapshot.Parameters, targetSnapshot.Parameters);
        if (diff.Count == 0)
        {
            return await RejectedAsync(
                adapter,
                "NoChanges",
                "Rollback target already matches the active strategy parameters.",
                cancellationToken).ConfigureAwait(false);
        }

        var rollbackParameters = new Dictionary<string, string>(
            targetSnapshot.Parameters,
            StringComparer.OrdinalIgnoreCase);
        var rollbackConfigVersion = BuildNextConfigVersion(currentSnapshot.ConfigVersion, "rollback");
        rollbackParameters["ConfigVersion"] = rollbackConfigVersion;
        var rollbackSnapshot = new StrategyOptionsSnapshot(
            targetSnapshot.StrategyId,
            rollbackConfigVersion,
            rollbackParameters);

        var version = await PersistAndApplyAsync(
            adapter,
            rollbackSnapshot,
            currentSnapshot,
            currentSnapshot.ConfigVersion,
            diff,
            "Rollback",
            source,
            actor,
            request.Reason,
            target.Id,
            cancellationToken).ConfigureAwait(false);

        await LogAuditAsync(
            "control-room strategy parameters rollback",
            adapter.StrategyId,
            version,
            diff,
            source,
            actor,
            request.Reason,
            success: true,
            cancellationToken).ConfigureAwait(false);

        return new StrategyParameterMutationResult(
            true,
            "Accepted",
            $"Strategy {adapter.StrategyId} parameters rolled back from {currentSnapshot.ConfigVersion} to {target.ConfigVersion}.",
            ToRecord(version),
            await GetSnapshotAsync(adapter.StrategyId, cancellationToken: cancellationToken).ConfigureAwait(false));
    }

    public async Task ApplyLatestAcceptedVersionsAsync(CancellationToken cancellationToken = default)
    {
        var latestVersions = await _repository
            .GetLatestByStrategyIdsAsync(_adapters.Keys.ToArray(), cancellationToken)
            .ConfigureAwait(false);

        foreach (var version in latestVersions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_adapters.TryGetValue(version.StrategyId, out var adapter))
            {
                continue;
            }

            try
            {
                adapter.ApplySnapshot(adapter.ParseSnapshot(version.SnapshotJson));
                _logger.LogInformation(
                    "Applied strategy parameter version {ConfigVersion} for {StrategyId}",
                    version.ConfigVersion,
                    version.StrategyId);
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "Failed to apply strategy parameter version {ConfigVersion} for {StrategyId}",
                    version.ConfigVersion,
                    version.StrategyId);
            }
        }
    }

    private async Task<StrategyParameterMutationResult> RejectedAsync(
        StrategyOptionsAdapter adapter,
        string status,
        string message,
        CancellationToken cancellationToken)
    {
        return new StrategyParameterMutationResult(
            false,
            status,
            message,
            null,
            await GetSnapshotAsync(adapter.StrategyId, cancellationToken: cancellationToken).ConfigureAwait(false));
    }

    private async Task<StrategyParameterVersion> PersistAndApplyAsync(
        StrategyOptionsAdapter adapter,
        StrategyOptionsSnapshot nextSnapshot,
        StrategyOptionsSnapshot revertSnapshot,
        string previousConfigVersion,
        IReadOnlyList<StrategyParameterDiff> diff,
        string changeType,
        string source,
        string actor,
        string? reason,
        Guid? rollbackSourceVersionId,
        CancellationToken cancellationToken)
    {
        var version = new StrategyParameterVersion(
            adapter.StrategyId,
            nextSnapshot.ConfigVersion,
            previousConfigVersion,
            JsonSerializer.Serialize(nextSnapshot.Parameters, JsonOptions),
            JsonSerializer.Serialize(diff, JsonOptions),
            changeType,
            source,
            actor,
            reason,
            DateTimeOffset.UtcNow,
            rollbackSourceVersionId);

        adapter.ApplySnapshot(nextSnapshot);
        try
        {
            await _repository.AddAsync(version, cancellationToken).ConfigureAwait(false);
            await _unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            adapter.ApplySnapshot(revertSnapshot);
            throw;
        }

        return version;
    }

    private async Task LogAuditAsync(
        string commandName,
        string strategyId,
        StrategyParameterVersion version,
        IReadOnlyList<StrategyParameterDiff> diff,
        string source,
        string actor,
        string? reason,
        bool success,
        CancellationToken cancellationToken)
    {
        var payload = new
        {
            source,
            actor,
            strategyId,
            versionId = version.Id,
            version.ConfigVersion,
            version.PreviousConfigVersion,
            version.ChangeType,
            version.RollbackSourceVersionId,
            reason,
            diff
        };

        await _auditLogger.LogAsync(
            new CommandAuditEntry(
                commandName,
                JsonSerializer.Serialize(payload, JsonOptions),
                actor,
                success,
                success ? 0 : 1,
                0,
                DateTimeOffset.UtcNow),
            cancellationToken).ConfigureAwait(false);
    }

    private StrategyOptionsAdapter ResolveAdapter(string strategyId)
    {
        if (string.IsNullOrWhiteSpace(strategyId)
            || !_adapters.TryGetValue(strategyId.Trim(), out var adapter))
        {
            throw new ArgumentException($"Unsupported strategy id: {strategyId}", nameof(strategyId));
        }

        return adapter;
    }

    private static StrategyParameterVersionRecord ToRecord(StrategyParameterVersion version)
    {
        var diff = string.IsNullOrWhiteSpace(version.DiffJson)
            ? []
            : JsonSerializer.Deserialize<IReadOnlyList<StrategyParameterDiff>>(version.DiffJson, JsonOptions) ?? [];

        return new StrategyParameterVersionRecord(
            version.Id,
            version.StrategyId,
            version.ConfigVersion,
            version.PreviousConfigVersion,
            version.ChangeType,
            version.Source,
            version.Actor,
            version.Reason,
            version.CreatedAtUtc,
            diff,
            version.RollbackSourceVersionId);
    }

    private static string NormalizeActor(string? actor)
        => string.IsNullOrWhiteSpace(actor) ? Environment.UserName : actor.Trim();

    private static string NormalizeSource(string? source)
        => string.IsNullOrWhiteSpace(source) ? "control-room-api" : source.Trim();

    private static StrategyOptionsAdapter CreateAdapter<TOptions>(
        string strategyId,
        IOptionsMonitor<TOptions> monitor,
        IOptionsMonitorCache<TOptions> cache,
        Action<TOptions> validate)
        where TOptions : class, new()
    {
        var parameterTypes = GetOptionProperties(typeof(TOptions))
            .ToDictionary(
                property => property.Name,
                property => (Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType).Name,
                StringComparer.OrdinalIgnoreCase);

        return new StrategyOptionsAdapter(
            strategyId,
            parameterTypes,
            () => SnapshotFromOptions(strategyId, monitor.CurrentValue),
            snapshot =>
            {
                var options = OptionsFromSnapshot<TOptions>(snapshot);
                validate(options);
                cache.TryRemove(Options.DefaultName);
                if (!cache.TryAdd(Options.DefaultName, options))
                {
                    throw new InvalidOperationException($"Unable to update options cache for {strategyId}.");
                }
            },
            snapshot =>
            {
                var options = OptionsFromSnapshot<TOptions>(snapshot);
                validate(options);
                return SnapshotFromOptions(strategyId, options);
            },
            changes =>
            {
                var current = CloneOptions(monitor.CurrentValue);
                var previousSnapshot = SnapshotFromOptions(strategyId, current);
                ApplyChanges(current, changes);
                var nextVersion = BuildNextConfigVersion(previousSnapshot.ConfigVersion, "params");
                typeof(TOptions).GetProperty("ConfigVersion")?.SetValue(current, nextVersion);
                validate(current);
                var nextSnapshot = SnapshotFromOptions(strategyId, current);
                var diff = BuildDiff(previousSnapshot.Parameters, nextSnapshot.Parameters);
                return (nextSnapshot, diff);
            });
    }

    private static StrategyOptionsSnapshot SnapshotFromOptions<TOptions>(string strategyId, TOptions options)
        where TOptions : class
    {
        var parameters = GetOptionProperties(typeof(TOptions))
            .ToDictionary(
                property => property.Name,
                property => FormatValue(property.GetValue(options)),
                StringComparer.OrdinalIgnoreCase);

        var configVersion = parameters.TryGetValue("ConfigVersion", out var version) && !string.IsNullOrWhiteSpace(version)
            ? version
            : "v1";

        return new StrategyOptionsSnapshot(strategyId, configVersion, parameters);
    }

    private static TOptions OptionsFromSnapshot<TOptions>(StrategyOptionsSnapshot snapshot)
        where TOptions : class, new()
    {
        var options = new TOptions();
        ApplyChanges(options, snapshot.Parameters, allowConfigVersion: true);
        return options;
    }

    private static TOptions CloneOptions<TOptions>(TOptions source)
        where TOptions : class, new()
    {
        var clone = new TOptions();
        foreach (var property in GetOptionProperties(typeof(TOptions)))
        {
            property.SetValue(clone, property.GetValue(source));
        }

        return clone;
    }

    private static void ApplyChanges<TOptions>(
        TOptions options,
        IReadOnlyDictionary<string, string> changes,
        bool allowConfigVersion = false)
        where TOptions : class
    {
        var properties = GetOptionProperties(typeof(TOptions))
            .ToDictionary(property => property.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var (name, rawValue) in changes)
        {
            if (!properties.TryGetValue(name, out var property))
            {
                throw new ArgumentException($"Unknown parameter '{name}'.", nameof(changes));
            }

            if (!allowConfigVersion && string.Equals(property.Name, "ConfigVersion", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("ConfigVersion is generated by the parameter workflow.", nameof(changes));
            }

            property.SetValue(options, ConvertValue(rawValue, property.PropertyType, property.Name));
        }
    }

    private static object ConvertValue(string rawValue, Type targetType, string parameterName)
    {
        var value = rawValue.Trim();
        var type = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (type == typeof(string))
        {
            return value;
        }

        if (type == typeof(string[]))
        {
            return value
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToArray();
        }

        if (type == typeof(bool))
        {
            if (bool.TryParse(value, out var parsed))
            {
                return parsed;
            }

            throw new FormatException($"{parameterName} must be true or false.");
        }

        if (type == typeof(int))
        {
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            {
                return parsed;
            }

            throw new FormatException($"{parameterName} must be an integer.");
        }

        if (type == typeof(decimal))
        {
            if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
            {
                return parsed;
            }

            throw new FormatException($"{parameterName} must be a decimal number.");
        }

        if (type.IsEnum)
        {
            return Enum.Parse(type, value, ignoreCase: true);
        }

        throw new NotSupportedException($"{parameterName} type {type.Name} is not supported.");
    }

    private static IReadOnlyList<PropertyInfo> GetOptionProperties(Type type)
    {
        return type
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.CanRead && property.CanWrite)
            .OrderBy(property => property.Name, StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyList<StrategyParameterDiff> BuildDiff(
        IReadOnlyDictionary<string, string> previous,
        IReadOnlyDictionary<string, string> next)
    {
        return next
            .Where(item => !string.Equals(item.Key, "ConfigVersion", StringComparison.OrdinalIgnoreCase))
            .Where(item => previous.TryGetValue(item.Key, out var previousValue)
                && !string.Equals(previousValue, item.Value, StringComparison.Ordinal))
            .Select(item => new StrategyParameterDiff(item.Key, previous[item.Key], item.Value))
            .OrderBy(item => item.Name, StringComparer.Ordinal)
            .ToArray();
    }

    private static string BuildNextConfigVersion(string currentVersion, string prefix)
    {
        var baseVersion = string.IsNullOrWhiteSpace(currentVersion) ? "v1" : currentVersion.Trim();
        var suffix = $"{prefix}.{DateTimeOffset.UtcNow:yyyyMMddHHmmss}.{Guid.NewGuid():N}"[..31];
        var baseLength = Math.Max(1, 63 - suffix.Length);
        var normalizedBase = baseVersion.Length <= baseLength ? baseVersion : baseVersion[..baseLength];
        return $"{normalizedBase}.{suffix}";
    }

    private static string FormatValue(object? value)
    {
        return value switch
        {
            null => string.Empty,
            string[] strings => string.Join(",", strings),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty
        };
    }

    private sealed record StrategyOptionsSnapshot(
        string StrategyId,
        string ConfigVersion,
        Dictionary<string, string> Parameters);

    private sealed class StrategyOptionsAdapter(
        string strategyId,
        IReadOnlyDictionary<string, string> parameterTypes,
        Func<StrategyOptionsSnapshot> getSnapshot,
        Action<StrategyOptionsSnapshot> applySnapshot,
        Func<StrategyOptionsSnapshot, StrategyOptionsSnapshot> parseSnapshot,
        Func<IReadOnlyDictionary<string, string>, (StrategyOptionsSnapshot Snapshot, IReadOnlyList<StrategyParameterDiff> Diff)> buildUpdatedSnapshot)
    {
        public string StrategyId { get; } = strategyId;

        public StrategyOptionsSnapshot GetSnapshot() => getSnapshot();

        public string GetConfigVersion() => getSnapshot().ConfigVersion;

        public IReadOnlyList<StrategyParameterValue> GetParameters()
        {
            return getSnapshot()
                .Parameters
                .Where(item => !string.Equals(item.Key, "ConfigVersion", StringComparison.OrdinalIgnoreCase))
                .Select(item => new StrategyParameterValue(
                    item.Key,
                    item.Value,
                    parameterTypes.TryGetValue(item.Key, out var typeName) ? typeName : "String",
                    true))
                .OrderBy(item => item.Name, StringComparer.Ordinal)
                .ToArray();
        }

        public (StrategyOptionsSnapshot Snapshot, IReadOnlyList<StrategyParameterDiff> Diff) BuildUpdatedSnapshot(
            IReadOnlyDictionary<string, string> changes)
            => buildUpdatedSnapshot(changes);

        public void ApplySnapshot(StrategyOptionsSnapshot snapshot) => applySnapshot(snapshot);

        public StrategyOptionsSnapshot ParseSnapshot(string snapshotJson)
        {
            var parameters = JsonSerializer.Deserialize<Dictionary<string, string>>(snapshotJson, JsonOptions)
                ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            return parseSnapshot(new StrategyOptionsSnapshot(
                StrategyId,
                parameters.TryGetValue("ConfigVersion", out var configVersion) ? configVersion : "v1",
                new Dictionary<string, string>(parameters, StringComparer.OrdinalIgnoreCase)));
        }
    }
}
