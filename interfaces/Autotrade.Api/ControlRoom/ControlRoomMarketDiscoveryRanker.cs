namespace Autotrade.Api.ControlRoom;

public static class ControlRoomMarketDiscoveryRanker
{
    private const decimal MinimumInterestingLiquidity = 1_000m;
    private const decimal MinimumInterestingVolume24h = 500m;
    private const decimal MinimumInterestingSignalScore = 0.25m;
    private const decimal MaximumInterestingSpread = 0.08m;
    private const decimal LiquidityNormalizer = 1_000_000m;
    private const decimal VolumeNormalizer = 500_000m;

    public static IReadOnlyList<ControlRoomMarketDto> EnrichMarkets(
        IReadOnlyList<ControlRoomMarketDto> markets,
        DateTimeOffset now)
    {
        return markets.Select(market => EnrichMarket(market, now)).ToArray();
    }

    public static ControlRoomMarketDto EnrichMarket(ControlRoomMarketDto market, DateTimeOffset now)
    {
        var unsuitableReasons = BuildUnsuitableReasons(market, now);
        var score = CalculateRankScore(market, now, unsuitableReasons.Count > 0);

        return market with
        {
            RankScore = score,
            RankReason = BuildRankReason(market, now),
            UnsuitableReasons = unsuitableReasons
        };
    }

    public static IReadOnlyList<ControlRoomMarketDto> FilterMarkets(
        IReadOnlyList<ControlRoomMarketDto> markets,
        ControlRoomMarketDiscoveryQuery query,
        DateTimeOffset now)
    {
        var filtered = markets.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var needle = query.Search.Trim();
            filtered = filtered.Where(market =>
                market.Name.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
                market.MarketId.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
                market.ConditionId.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
                (market.Slug?.Contains(needle, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        if (!string.IsNullOrWhiteSpace(query.Category) &&
            !string.Equals(query.Category, "all", StringComparison.OrdinalIgnoreCase))
        {
            filtered = filtered.Where(market => string.Equals(market.Category, query.Category, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(query.Status) &&
            !string.Equals(query.Status, "all", StringComparison.OrdinalIgnoreCase))
        {
            filtered = filtered.Where(market => string.Equals(market.Status, query.Status, StringComparison.OrdinalIgnoreCase));
        }

        if (query.AcceptingOrders is not null)
        {
            filtered = filtered.Where(market => market.AcceptingOrders == query.AcceptingOrders.Value);
        }

        var minLiquidity = NormalizeNonNegative(query.MinLiquidity);
        if (minLiquidity is not null)
        {
            filtered = filtered.Where(market => market.Liquidity >= minLiquidity.Value);
        }

        var minVolume = NormalizeNonNegative(query.MinVolume24h);
        if (minVolume is not null)
        {
            filtered = filtered.Where(market => market.Volume24h >= minVolume.Value);
        }

        var minSignal = NormalizeSignal(query.MinSignalScore);
        if (minSignal is not null)
        {
            filtered = filtered.Where(market => market.SignalScore >= minSignal.Value);
        }

        var maxDaysToExpiry = NormalizeMaxDays(query.MaxDaysToExpiry);
        if (maxDaysToExpiry is not null)
        {
            var latestExpiry = now.AddDays(maxDaysToExpiry.Value);
            filtered = filtered.Where(market =>
                market.ExpiresAtUtc is not null &&
                market.ExpiresAtUtc.Value >= now &&
                market.ExpiresAtUtc.Value <= latestExpiry);
        }

        return filtered.ToArray();
    }

    public static IReadOnlyList<ControlRoomMarketDto> SortMarkets(
        IReadOnlyList<ControlRoomMarketDto> markets,
        string? sort)
    {
        return (sort?.Trim().ToLowerInvariant()) switch
        {
            "expiry" => markets
                .OrderBy(market => market.ExpiresAtUtc ?? DateTimeOffset.MaxValue)
                .ThenByDescending(market => market.RankScore)
                .ThenBy(market => market.MarketId, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            "liquidity" => markets
                .OrderByDescending(market => market.Liquidity)
                .ThenByDescending(market => market.RankScore)
                .ThenBy(market => market.MarketId, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            "price" => markets
                .OrderByDescending(market => market.YesPrice is null ? 0m : Math.Abs(market.YesPrice.Value - 0.5m))
                .ThenByDescending(market => market.RankScore)
                .ThenBy(market => market.MarketId, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            "signal" => markets
                .OrderByDescending(market => market.SignalScore)
                .ThenByDescending(market => market.RankScore)
                .ThenBy(market => market.MarketId, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            "volume" => markets
                .OrderByDescending(market => market.Volume24h)
                .ThenByDescending(market => market.RankScore)
                .ThenBy(market => market.MarketId, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            _ => markets
                .OrderByDescending(market => market.RankScore)
                .ThenByDescending(market => market.SignalScore)
                .ThenByDescending(market => market.Volume24h)
                .ThenByDescending(market => market.Liquidity)
                .ThenBy(market => market.ExpiresAtUtc ?? DateTimeOffset.MaxValue)
                .ThenBy(market => market.MarketId, StringComparer.OrdinalIgnoreCase)
                .ToArray()
        };
    }

    private static IReadOnlyList<string> BuildUnsuitableReasons(ControlRoomMarketDto market, DateTimeOffset now)
    {
        var reasons = new List<string>();

        if (!string.Equals(market.Status, "Active", StringComparison.OrdinalIgnoreCase))
        {
            reasons.Add($"Market status is {market.Status}.");
        }

        if (!market.AcceptingOrders)
        {
            reasons.Add("Market is not accepting orders.");
        }

        if (market.ExpiresAtUtc is null)
        {
            reasons.Add("Expiry is unavailable.");
        }
        else if (market.ExpiresAtUtc.Value <= now)
        {
            reasons.Add("Market has expired.");
        }

        if (market.Liquidity < MinimumInterestingLiquidity)
        {
            reasons.Add($"Liquidity is below {MinimumInterestingLiquidity:0}.");
        }

        if (market.Volume24h < MinimumInterestingVolume24h)
        {
            reasons.Add($"24h volume is below {MinimumInterestingVolume24h:0}.");
        }

        if (market.SignalScore < MinimumInterestingSignalScore)
        {
            reasons.Add($"Signal score is below {MinimumInterestingSignalScore:0.##}.");
        }

        if (market.Spread is > MaximumInterestingSpread)
        {
            reasons.Add($"Spread is wider than {MaximumInterestingSpread:0.##}.");
        }

        return reasons;
    }

    private static decimal CalculateRankScore(
        ControlRoomMarketDto market,
        DateTimeOffset now,
        bool hasUnsuitableReasons)
    {
        var signalComponent = Math.Clamp(market.SignalScore, 0m, 1m) * 0.25m;
        var liquidityComponent = LogScale(market.Liquidity, LiquidityNormalizer) * 0.30m;
        var volumeComponent = LogScale(market.Volume24h, VolumeNormalizer) * 0.30m;
        var expiryComponent = CalculateExpiryScore(market.ExpiresAtUtc, now) * 0.10m;
        var spreadComponent = CalculateSpreadScore(market.Spread) * 0.05m;
        var acceptingComponent = market.AcceptingOrders && string.Equals(market.Status, "Active", StringComparison.OrdinalIgnoreCase)
            ? 0.05m
            : 0m;
        var unsuitablePenalty = hasUnsuitableReasons ? 0.18m : 0m;
        var blockedPenalty = !market.AcceptingOrders || !string.Equals(market.Status, "Active", StringComparison.OrdinalIgnoreCase)
            ? 0.30m
            : 0m;

        var score = signalComponent +
            liquidityComponent +
            volumeComponent +
            expiryComponent +
            spreadComponent +
            acceptingComponent -
            unsuitablePenalty -
            blockedPenalty;

        return Math.Round(Math.Clamp(score, 0m, 1m), 3);
    }

    private static string BuildRankReason(ControlRoomMarketDto market, DateTimeOffset now)
    {
        var factors = new List<string>
        {
            market.SignalScore >= 0.70m ? "strong signal" : market.SignalScore >= 0.45m ? "moderate signal" : "weak signal",
            market.Liquidity >= 25_000m ? "deep liquidity" : market.Liquidity >= MinimumInterestingLiquidity ? "usable liquidity" : "thin liquidity",
            market.Volume24h >= 10_000m ? "active 24h volume" : market.Volume24h >= MinimumInterestingVolume24h ? "some 24h volume" : "quiet 24h volume"
        };

        factors.Add(market.AcceptingOrders ? "accepting orders" : "not accepting orders");

        if (market.ExpiresAtUtc is null)
        {
            factors.Add("expiry unavailable");
        }
        else
        {
            var daysToExpiry = (market.ExpiresAtUtc.Value - now).TotalDays;
            factors.Add(daysToExpiry <= 0d
                ? "expired"
                : daysToExpiry < 1d
                    ? "expires within 24h"
                    : $"expires in {Math.Ceiling(daysToExpiry):0}d");
        }

        return string.Join("; ", factors);
    }

    private static decimal CalculateExpiryScore(DateTimeOffset? expiresAtUtc, DateTimeOffset now)
    {
        if (expiresAtUtc is null || expiresAtUtc.Value <= now)
        {
            return 0m;
        }

        var daysToExpiry = (decimal)(expiresAtUtc.Value - now).TotalDays;
        if (daysToExpiry <= 30m)
        {
            return 1m;
        }

        if (daysToExpiry >= 180m)
        {
            return 0.35m;
        }

        return Math.Clamp(1m - ((daysToExpiry - 30m) / 150m * 0.65m), 0.35m, 1m);
    }

    private static decimal CalculateSpreadScore(decimal? spread)
    {
        return spread switch
        {
            null => 0.5m,
            <= 0.02m => 1m,
            <= 0.05m => 0.70m,
            <= MaximumInterestingSpread => 0.40m,
            _ => 0m
        };
    }

    private static decimal LogScale(decimal value, decimal normalizer)
    {
        if (value <= 0m)
        {
            return 0m;
        }

        var scaled = Math.Log10((double)value + 1d) / Math.Log10((double)normalizer + 1d);
        return Math.Clamp((decimal)scaled, 0m, 1m);
    }

    private static decimal? NormalizeNonNegative(decimal? value)
    {
        return value is null ? null : Math.Max(0m, value.Value);
    }

    private static decimal? NormalizeSignal(decimal? value)
    {
        return value is null ? null : Math.Clamp(value.Value, 0m, 1m);
    }

    private static int? NormalizeMaxDays(int? value)
    {
        return value is null ? null : Math.Clamp(value.Value, 0, 3650);
    }
}
