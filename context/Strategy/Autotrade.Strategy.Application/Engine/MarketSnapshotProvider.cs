// ============================================================================
// 市场快照提供者
// ============================================================================
// 从市场目录和订单簿合成市场快照。
// ============================================================================

using Autotrade.MarketData.Application.Contract.Catalog;
using Autotrade.MarketData.Application.Contract.OrderBook;
using Autotrade.Strategy.Application.Contract.Strategies;
using Microsoft.Extensions.Logging;

namespace Autotrade.Strategy.Application.Engine;

/// <summary>
/// 市场快照提供者接口。
/// </summary>
public interface IMarketSnapshotProvider
{
    /// <summary>
    /// 获取指定市场的快照列表。
    /// </summary>
    Task<IReadOnlyList<MarketSnapshot>> GetSnapshotsAsync(IEnumerable<string> marketIds,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 市场快照提供者。
/// 从市场目录和订单簿合成市场快照。
/// </summary>
public sealed class MarketSnapshotProvider : IMarketSnapshotProvider
{
    private readonly IMarketCatalogReader _catalogReader;
    private readonly IOrderBookReader _orderBookReader;
    private readonly ILogger<MarketSnapshotProvider> _logger;

    public MarketSnapshotProvider(
        IMarketCatalogReader catalogReader,
        IOrderBookReader orderBookReader,
        ILogger<MarketSnapshotProvider> logger)
    {
        _catalogReader = catalogReader ?? throw new ArgumentNullException(nameof(catalogReader));
        _orderBookReader = orderBookReader ?? throw new ArgumentNullException(nameof(orderBookReader));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 获取指定市场的快照列表。
    /// </summary>
    public Task<IReadOnlyList<MarketSnapshot>> GetSnapshotsAsync(
        IEnumerable<string> marketIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(marketIds);

        var snapshots = new List<MarketSnapshot>();
        foreach (var marketId in marketIds)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            if (string.IsNullOrWhiteSpace(marketId))
            {
                continue;
            }

            var market = _catalogReader.GetMarket(marketId);
            if (market is null)
            {
                _logger.LogDebug("Market not found in catalog: {MarketId}", marketId);
                continue;
            }

            if (market.TokenIds.Count < 2)
            {
                _logger.LogWarning("Market {MarketId} has insufficient token ids: {Count}",
                    marketId, market.TokenIds.Count);
                continue;
            }

            var yesTokenId = market.TokenIds[0];
            var noTokenId = market.TokenIds[1];

            var yesTop = _orderBookReader.GetTopOfBook(yesTokenId);
            var noTop = _orderBookReader.GetTopOfBook(noTokenId);

            snapshots.Add(new MarketSnapshot(
                market,
                yesTop,
                noTop,
                DateTimeOffset.UtcNow));
        }

        return Task.FromResult<IReadOnlyList<MarketSnapshot>>(snapshots);
    }
}
