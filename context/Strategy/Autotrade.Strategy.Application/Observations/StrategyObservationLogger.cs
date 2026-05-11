using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Autotrade.Strategy.Application.Contract.Strategies;
using Autotrade.Strategy.Application.Engine;
using Autotrade.Strategy.Domain.Entities;
using Autotrade.Trading.Application.Contract.Execution;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Autotrade.Strategy.Application.Observations;

public sealed class StrategyObservationLogger : IStrategyObservationLogger
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IStrategyObservationRepository _repository;
    private readonly StrategyObservationOptions _options;
    private readonly StrategyEngineOptions _engineOptions;
    private readonly ExecutionOptions _executionOptions;
    private readonly ILogger<StrategyObservationLogger> _logger;
    private readonly ConcurrentDictionary<SkipBucketKey, SkipBucket> _skipBuckets = new();

    public StrategyObservationLogger(
        IStrategyObservationRepository repository,
        IOptions<StrategyObservationOptions> options,
        IOptions<StrategyEngineOptions> engineOptions,
        IOptions<ExecutionOptions> executionOptions,
        ILogger<StrategyObservationLogger> logger)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _engineOptions = engineOptions?.Value ?? throw new ArgumentNullException(nameof(engineOptions));
        _executionOptions = executionOptions?.Value ?? throw new ArgumentNullException(nameof(executionOptions));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task LogAsync(StrategyObservation observation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(observation);

        if (!_options.Enabled)
        {
            return;
        }

        _options.Validate();

        var normalized = Normalize(observation);
        if (ShouldAggregateSkip(normalized))
        {
            await RecordAggregatedSkipAsync(normalized, cancellationToken).ConfigureAwait(false);
            return;
        }

        await PersistAsync(ToEntity(normalized), cancellationToken).ConfigureAwait(false);
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        if (_skipBuckets.IsEmpty)
        {
            return;
        }

        var flushed = new List<StrategyObservationLog>();
        foreach (var entry in _skipBuckets.ToArray())
        {
            if (_skipBuckets.TryRemove(entry.Key, out var bucket))
            {
                flushed.Add(CreateAggregateObservation(entry.Key, bucket));
            }
        }

        if (flushed.Count == 0)
        {
            return;
        }

        try
        {
            await _repository.AddRangeAsync(flushed, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to flush strategy observation aggregates.");
        }
    }

    private StrategyObservation Normalize(StrategyObservation observation)
    {
        var correlationId = string.IsNullOrWhiteSpace(observation.CorrelationId)
            ? Activity.Current?.Id
            : observation.CorrelationId;

        var executionMode = string.IsNullOrWhiteSpace(observation.ExecutionMode)
            ? _executionOptions.Mode.ToString()
            : observation.ExecutionMode;

        var configVersion = string.IsNullOrWhiteSpace(observation.ConfigVersion)
            ? _engineOptions.ConfigVersion
            : observation.ConfigVersion;

        return observation with
        {
            StrategyId = observation.StrategyId.Trim(),
            MarketId = string.IsNullOrWhiteSpace(observation.MarketId) ? null : observation.MarketId.Trim(),
            Phase = observation.Phase.Trim(),
            Outcome = observation.Outcome.Trim(),
            ReasonCode = observation.ReasonCode.Trim(),
            FeaturesJson = NormalizeJson(observation.FeaturesJson),
            StateJson = NormalizeJson(observation.StateJson),
            CorrelationId = string.IsNullOrWhiteSpace(correlationId) ? null : correlationId,
            ConfigVersion = string.IsNullOrWhiteSpace(configVersion) ? "unknown" : configVersion.Trim(),
            ExecutionMode = string.IsNullOrWhiteSpace(executionMode) ? null : executionMode.Trim(),
            TimestampUtc = observation.TimestampUtc == default ? DateTimeOffset.UtcNow : observation.TimestampUtc
        };
    }

    private static string? NormalizeJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        var trimmed = json.Trim();
        try
        {
            using var _ = JsonDocument.Parse(trimmed);
            return trimmed;
        }
        catch (JsonException)
        {
            return JsonSerializer.Serialize(new { raw = trimmed }, JsonOptions);
        }
    }

    private bool ShouldAggregateSkip(StrategyObservation observation)
    {
        return _options.AggregateSkips
            && string.Equals(observation.Outcome, "Skipped", StringComparison.OrdinalIgnoreCase);
    }

    private async Task RecordAggregatedSkipAsync(
        StrategyObservation observation,
        CancellationToken cancellationToken)
    {
        var key = SkipBucketKey.From(observation, TimeSpan.FromSeconds(_options.SkipAggregationWindowSeconds));
        var bucket = _skipBuckets.GetOrAdd(key, static _ => new SkipBucket());
        var count = bucket.Increment(observation.FeaturesJson);

        await FlushExpiredBucketsAsync(key.WindowStartUtc, cancellationToken).ConfigureAwait(false);

        if (_options.SkipSampleEvery > 0 && count % _options.SkipSampleEvery == 1)
        {
            var sampled = observation with
            {
                Outcome = "SkippedSampled",
                ReasonCode = observation.ReasonCode
            };

            await PersistAsync(ToEntity(sampled), cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task FlushExpiredBucketsAsync(DateTimeOffset activeWindowStartUtc, CancellationToken cancellationToken)
    {
        var expired = _skipBuckets
            .Where(entry => entry.Key.WindowStartUtc < activeWindowStartUtc)
            .ToList();

        var observations = new List<StrategyObservationLog>();

        foreach (var entry in expired)
        {
            if (!_skipBuckets.TryRemove(entry.Key, out var bucket))
            {
                continue;
            }

            observations.Add(CreateAggregateObservation(entry.Key, bucket));
        }

        if (observations.Count == 0)
        {
            return;
        }

        try
        {
            await _repository.AddRangeAsync(observations, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to flush expired strategy observation aggregates.");
        }
    }

    private async Task PersistAsync(StrategyObservationLog observation, CancellationToken cancellationToken)
    {
        try
        {
            await _repository.AddAsync(observation, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to persist strategy observation {StrategyId}/{Phase}/{ReasonCode}.",
                observation.StrategyId,
                observation.Phase,
                observation.ReasonCode);
        }
    }

    private static StrategyObservationLog ToEntity(StrategyObservation observation)
    {
        return new StrategyObservationLog(
            observation.StrategyId,
            observation.MarketId,
            observation.Phase,
            observation.Outcome,
            observation.ReasonCode,
            observation.FeaturesJson,
            observation.StateJson,
            observation.CorrelationId,
            observation.ConfigVersion,
            observation.ExecutionMode,
            observation.TimestampUtc);
    }

    private static StrategyObservationLog CreateAggregateObservation(SkipBucketKey key, SkipBucket bucket)
    {
        var featuresJson = JsonSerializer.Serialize(new
        {
            skipCount = bucket.Count,
            sampledFeatureJson = bucket.LastSampledFeaturesJson,
            windowStartUtc = key.WindowStartUtc,
            windowEndUtc = key.WindowStartUtc.Add(key.Window)
        }, JsonOptions);

        return new StrategyObservationLog(
            key.StrategyId,
            key.MarketId,
            key.Phase,
            "SkippedAggregate",
            key.ReasonCode,
            featuresJson,
            null,
            null,
            key.ConfigVersion,
            key.ExecutionMode,
            key.WindowStartUtc);
    }

    private sealed class SkipBucket
    {
        private long _count;

        public long Count => Interlocked.Read(ref _count);

        public string? LastSampledFeaturesJson { get; private set; }

        public long Increment(string? featuresJson)
        {
            LastSampledFeaturesJson = featuresJson ?? LastSampledFeaturesJson;
            return Interlocked.Increment(ref _count);
        }
    }

    private sealed record SkipBucketKey(
        string StrategyId,
        string? MarketId,
        string Phase,
        string ReasonCode,
        string ConfigVersion,
        string? ExecutionMode,
        DateTimeOffset WindowStartUtc,
        TimeSpan Window)
    {
        public static SkipBucketKey From(StrategyObservation observation, TimeSpan window)
        {
            var unixSeconds = observation.TimestampUtc.ToUnixTimeSeconds();
            var bucketSeconds = (unixSeconds / (long)window.TotalSeconds) * (long)window.TotalSeconds;
            var windowStartUtc = DateTimeOffset.FromUnixTimeSeconds(bucketSeconds);

            return new SkipBucketKey(
                observation.StrategyId,
                observation.MarketId,
                observation.Phase,
                observation.ReasonCode,
                observation.ConfigVersion,
                observation.ExecutionMode,
                windowStartUtc,
                window);
        }
    }
}
