using System.Collections.Concurrent;
using Autotrade.MarketData.Application.Contract.Spot;
using Microsoft.Extensions.Options;

namespace Autotrade.MarketData.Application.Spot;

public sealed class InMemorySpotPriceStore : ISpotPriceStore
{
    private readonly ConcurrentDictionary<string, SymbolSeries> _series = new(StringComparer.OrdinalIgnoreCase);
    private readonly SpotPriceStoreOptions _options;

    public InMemorySpotPriceStore(IOptions<SpotPriceStoreOptions> options)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();
    }

    public SpotPriceUpdateResult UpdateTick(SpotPriceTick tick)
    {
        var normalized = NormalizeTick(tick, out var rejectedReason);
        if (normalized is null)
        {
            return new SpotPriceUpdateResult(false, false, null, rejectedReason);
        }

        var series = _series.GetOrAdd(normalized.Symbol, _ => new SymbolSeries());
        var updatedLatest = series.Add(normalized, _options);
        return new SpotPriceUpdateResult(true, updatedLatest, normalized);
    }

    public bool TryGetLatest(string symbol, out SpotPriceTick tick)
    {
        tick = default!;
        return TryGetSeries(symbol, out var series) && series.TryGetLatest(out tick);
    }

    public bool TryGetLatest(string symbol, TimeSpan maxAge, DateTimeOffset nowUtc, out SpotPriceTick tick)
    {
        tick = default!;
        if (!TryGetLatest(symbol, out var latest))
        {
            return false;
        }

        var now = nowUtc.ToUniversalTime();
        var age = now >= latest.TimestampUtc
            ? now - latest.TimestampUtc
            : latest.TimestampUtc - now;

        if (age > maxAge)
        {
            return false;
        }

        tick = latest;
        return true;
    }

    public bool TryGetAtOrBefore(
        string symbol,
        DateTimeOffset timestampUtc,
        TimeSpan maxLookback,
        out SpotPriceTick tick)
    {
        tick = default!;
        return TryGetSeries(symbol, out var series)
               && series.TryGetAtOrBefore(timestampUtc.ToUniversalTime(), maxLookback, out tick);
    }

    public IReadOnlyList<SpotPriceTick> GetRecentTicks(string symbol, int maxCount)
    {
        if (maxCount <= 0 || !TryGetSeries(symbol, out var series))
        {
            return Array.Empty<SpotPriceTick>();
        }

        return series.GetRecentTicks(maxCount);
    }

    public IReadOnlyList<SpotBar> GetRecentBars(string symbol, TimeSpan interval, DateTimeOffset sinceUtc)
    {
        if (interval <= TimeSpan.Zero || !TryGetSeries(symbol, out var series))
        {
            return Array.Empty<SpotBar>();
        }

        return series.GetBars(interval, sinceUtc.ToUniversalTime());
    }

    private static SpotPriceTick? NormalizeTick(SpotPriceTick tick, out string? rejectedReason)
    {
        rejectedReason = null;

        if (string.IsNullOrWhiteSpace(tick.Symbol))
        {
            rejectedReason = "symbol_empty";
            return null;
        }

        if (tick.Price <= 0m)
        {
            rejectedReason = "price_not_positive";
            return null;
        }

        var source = string.IsNullOrWhiteSpace(tick.Source) ? "unknown" : tick.Source.Trim();
        return tick with
        {
            Symbol = tick.Symbol.Trim().ToUpperInvariant(),
            TimestampUtc = tick.TimestampUtc.ToUniversalTime(),
            Source = source
        };
    }

    private bool TryGetSeries(string symbol, out SymbolSeries series)
    {
        series = default!;
        if (string.IsNullOrWhiteSpace(symbol))
        {
            return false;
        }

        return _series.TryGetValue(symbol.Trim().ToUpperInvariant(), out series!);
    }

    private sealed class SymbolSeries
    {
        private readonly object _lock = new();
        private readonly SortedDictionary<DateTimeOffset, SpotPriceTick> _ticks = [];
        private SpotPriceTick? _latest;

        public bool Add(SpotPriceTick tick, SpotPriceStoreOptions options)
        {
            lock (_lock)
            {
                _ticks[tick.TimestampUtc] = tick;
                var updatedLatest = _latest is null || tick.TimestampUtc >= _latest.TimestampUtc;
                if (updatedLatest)
                {
                    _latest = tick;
                }

                Trim(options, tick.TimestampUtc);
                return updatedLatest;
            }
        }

        public bool TryGetLatest(out SpotPriceTick tick)
        {
            lock (_lock)
            {
                tick = _latest!;
                return _latest is not null;
            }
        }

        public bool TryGetAtOrBefore(DateTimeOffset timestampUtc, TimeSpan maxLookback, out SpotPriceTick tick)
        {
            lock (_lock)
            {
                foreach (var item in _ticks.Reverse())
                {
                    if (item.Key > timestampUtc)
                    {
                        continue;
                    }

                    if (timestampUtc - item.Key > maxLookback)
                    {
                        break;
                    }

                    tick = item.Value;
                    return true;
                }

                tick = default!;
                return false;
            }
        }

        public IReadOnlyList<SpotPriceTick> GetRecentTicks(int maxCount)
        {
            lock (_lock)
            {
                return _ticks.Values
                    .Reverse()
                    .Take(maxCount)
                    .Reverse()
                    .ToArray();
            }
        }

        public IReadOnlyList<SpotBar> GetBars(TimeSpan interval, DateTimeOffset sinceUtc)
        {
            lock (_lock)
            {
                return _ticks.Values
                    .Where(tick => tick.TimestampUtc >= sinceUtc)
                    .GroupBy(tick => AlignStart(tick.TimestampUtc, interval))
                    .OrderBy(group => group.Key)
                    .Select(group =>
                    {
                        var ordered = group.OrderBy(tick => tick.TimestampUtc).ToArray();
                        return new SpotBar(
                            ordered[0].Symbol,
                            group.Key,
                            group.Key.Add(interval),
                            ordered[0].Price,
                            ordered.Max(tick => tick.Price),
                            ordered.Min(tick => tick.Price),
                            ordered[^1].Price,
                            ordered[^1].Source,
                            ordered.Length);
                    })
                    .ToArray();
            }
        }

        private void Trim(SpotPriceStoreOptions options, DateTimeOffset newestTimestampUtc)
        {
            var cutoff = newestTimestampUtc.AddMinutes(-options.MaxHistoryMinutes);
            while (_ticks.Count > options.MaxTicksPerSymbol || (_ticks.Count > 0 && _ticks.First().Key < cutoff))
            {
                _ticks.Remove(_ticks.First().Key);
            }
        }

        private static DateTimeOffset AlignStart(DateTimeOffset timestampUtc, TimeSpan interval)
        {
            var ticks = timestampUtc.UtcTicks / interval.Ticks * interval.Ticks;
            return new DateTimeOffset(ticks, TimeSpan.Zero);
        }
    }
}
