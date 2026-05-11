using System.Collections.Concurrent;
using Autotrade.MarketData.Domain.Entities;
using Autotrade.MarketData.Domain.Shared.Enums;
using Microsoft.Extensions.Logging;

namespace Autotrade.MarketData.Application.Catalog;

/// <summary>
/// 市场目录服务，负责刷新市场元数据并提供过滤功能。
/// </summary>
public sealed class MarketCatalog : IMarketCatalog
{
    private readonly ConcurrentDictionary<string, MarketInfo> _markets = new();
    private readonly ILogger<MarketCatalog> _logger;
    private DateTimeOffset _lastRefreshUtc = DateTimeOffset.MinValue;

    public MarketCatalog(ILogger<MarketCatalog> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public DateTimeOffset LastRefreshUtc => _lastRefreshUtc;

    /// <inheritdoc />
    public int Count => _markets.Count;

    /// <inheritdoc />
    public void UpdateMarkets(IEnumerable<MarketInfo> markets)
    {
        ArgumentNullException.ThrowIfNull(markets);

        var added = 0;
        var updated = 0;

        foreach (var market in markets)
        {
            if (_markets.TryAdd(market.MarketId, market))
            {
                added++;
            }
            else
            {
                _markets[market.MarketId] = market;
                updated++;
            }
        }

        _lastRefreshUtc = DateTimeOffset.UtcNow;

        _logger.LogInformation("市场目录更新: 新增={Added}, 更新={Updated}, 总数={Total}",
            added, updated, _markets.Count);
    }

    /// <inheritdoc />
    public MarketInfo? GetMarket(string marketId)
    {
        return _markets.TryGetValue(marketId, out var market) ? market : null;
    }

    /// <inheritdoc />
    public IReadOnlyList<MarketInfo> GetAllMarkets()
    {
        return _markets.Values.ToList();
    }

    /// <inheritdoc />
    public IReadOnlyList<MarketInfo> GetActiveMarkets()
    {
        return _markets.Values
            .Where(m => m.Status == MarketStatus.Active)
            .ToList();
    }

    /// <inheritdoc />
    public IReadOnlyList<MarketInfo> GetMarketsByCategory(string category)
    {
        return _markets.Values
            .Where(m => string.Equals(m.Category, category, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <inheritdoc />
    public IReadOnlyList<MarketInfo> GetExpiringMarkets(TimeSpan within)
    {
        var threshold = DateTimeOffset.UtcNow.Add(within);
        return _markets.Values
            .Where(m => m.ExpiresAtUtc.HasValue && m.ExpiresAtUtc <= threshold)
            .OrderBy(m => m.ExpiresAtUtc)
            .ToList();
    }

    /// <inheritdoc />
    public IReadOnlyList<MarketInfo> GetLiquidMarkets(decimal minVolume)
    {
        return _markets.Values
            .Where(m => m.Volume24h >= minVolume)
            .OrderByDescending(m => m.Volume24h)
            .ToList();
    }

    /// <inheritdoc />
    public IReadOnlyList<MarketInfo> Get15MinBtcMarkets()
    {
        // 15 分钟 BTC Up/Down 市场的识别
        // 参考 polymarket-orderbook-watcher 的逻辑
        return _markets.Values
            .Where(m => m.Slug?.Contains("btc-updown-15m", StringComparison.OrdinalIgnoreCase) == true
                     || m.Category?.Contains("15m", StringComparison.OrdinalIgnoreCase) == true)
            .ToList();
    }

    /// <inheritdoc />
    public void Clear()
    {
        _markets.Clear();
        _lastRefreshUtc = DateTimeOffset.MinValue;
    }
}

/// <summary>
/// 市场目录接口。
/// </summary>
public interface IMarketCatalog
{
    /// <summary>
    /// 最后刷新时间。
    /// </summary>
    DateTimeOffset LastRefreshUtc { get; }

    /// <summary>
    /// 市场数量。
    /// </summary>
    int Count { get; }

    /// <summary>
    /// 更新市场列表。
    /// </summary>
    void UpdateMarkets(IEnumerable<MarketInfo> markets);

    /// <summary>
    /// 获取指定市场。
    /// </summary>
    MarketInfo? GetMarket(string marketId);

    /// <summary>
    /// 获取所有市场。
    /// </summary>
    IReadOnlyList<MarketInfo> GetAllMarkets();

    /// <summary>
    /// 获取活跃市场。
    /// </summary>
    IReadOnlyList<MarketInfo> GetActiveMarkets();

    /// <summary>
    /// 按类别获取市场。
    /// </summary>
    IReadOnlyList<MarketInfo> GetMarketsByCategory(string category);

    /// <summary>
    /// 获取即将到期的市场。
    /// </summary>
    IReadOnlyList<MarketInfo> GetExpiringMarkets(TimeSpan within);

    /// <summary>
    /// 获取高流动性市场。
    /// </summary>
    IReadOnlyList<MarketInfo> GetLiquidMarkets(decimal minVolume);

    /// <summary>
    /// 获取 15 分钟 BTC Up/Down 市场。
    /// </summary>
    IReadOnlyList<MarketInfo> Get15MinBtcMarkets();

    /// <summary>
    /// 清除所有市场数据。
    /// </summary>
    void Clear();
}

/// <summary>
/// 市场信息 DTO。
/// </summary>
public sealed record MarketInfo
{
    public required string MarketId { get; init; }
    public required string ConditionId { get; init; }
    public required string Name { get; init; }
    public string? Slug { get; init; }
    public string? Category { get; init; }
    public MarketStatus Status { get; init; }
    public DateTimeOffset? ExpiresAtUtc { get; init; }
    public decimal Volume24h { get; init; }
    public decimal Liquidity { get; init; }

    /// <summary>
    /// Token IDs (用于 CLOB 订阅)。
    /// tokenIds[0] = Yes/Up, tokenIds[1] = No/Down
    /// </summary>
    public IReadOnlyList<string> TokenIds { get; init; } = Array.Empty<string>();
}
