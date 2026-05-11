using System.Diagnostics;
using System.Text.Json;
using Autotrade.Infra.BackgroundJobs.Core;
using Autotrade.MarketData.Application.Catalog;
using Autotrade.MarketData.Application.Contract.Catalog;
using Autotrade.MarketData.Application.Contract.Repositories;
using Autotrade.MarketData.Domain.Shared.Enums;
using Autotrade.Polymarket.Abstractions;
using Autotrade.Polymarket.Models;
using Hangfire;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MarketInfo = Autotrade.MarketData.Application.Catalog.MarketInfo;

namespace Autotrade.MarketData.Infra.BackgroundJobs.Jobs;

/// <summary>
/// 定时从 Gamma API 刷新市场元数据，并更新内存 MarketCatalog（Hangfire 任务）。
/// </summary>
[DisableConcurrentExecution(timeoutInSeconds: 60 * 60)]
public sealed class MarketCatalogSyncJob : JobBase<MarketCatalogSyncJob>
{
    private const int PersistBatchSize = 500;

    private readonly IPolymarketGammaClient _gammaClient;
    private readonly IMarketCatalog _catalog;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly MarketCatalogSyncOptions _options;

    public MarketCatalogSyncJob(
        IPolymarketGammaClient gammaClient,
        IMarketCatalog catalog,
        IServiceScopeFactory scopeFactory,
        IOptions<MarketCatalogSyncOptions> options,
        Microsoft.Extensions.Logging.ILogger<MarketCatalogSyncJob> logger)
        : base(logger)
    {
        _gammaClient = gammaClient ?? throw new ArgumentNullException(nameof(gammaClient));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    protected override async Task ExecuteJobAsync(CancellationToken cancellationToken)
    {
        _options.Validate();

        // 进程首次启动：尽量从 DB 预热，提升恢复速度（best-effort）
        if (_catalog.Count == 0)
        {
            await PreloadFromDatabaseAsync(cancellationToken).ConfigureAwait(false);
        }

        await RefreshOnceAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task PreloadFromDatabaseAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var repository = scope.ServiceProvider.GetRequiredService<IMarketRepository>();

            var markets = await repository.GetAllAsync(cancellationToken).ConfigureAwait(false);
            if (markets.Count == 0)
            {
                return;
            }

            var marketInfos = markets.Select(m => new MarketInfo
            {
                MarketId = m.MarketId,
                ConditionId = string.Empty, // 数据库不存储 ConditionId，后续 API 同步会补充
                Name = m.Name,
                Status = m.Status,
                ExpiresAtUtc = m.ExpiresAtUtc
            }).ToList();

            _catalog.UpdateMarkets(marketInfos);

            Logger.LogInformation(
                "Markets preloaded from database: count={Count}, catalogCount={CatalogCount}",
                markets.Count,
                _catalog.Count);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // no-op
        }
        catch (Exception ex)
        {
            // 预热失败不阻塞后续 API 同步
            Logger.LogWarning(ex, "Failed to preload markets from database");
        }
    }

    private async Task RefreshOnceAsync(CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var total = 0;
        var mapped = new List<MarketInfo>(capacity: _options.PageSize * 2);

        for (var page = 0; page < _options.MaxPages; page++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var offset = page * _options.PageSize;
            var result = await _gammaClient.ListMarketsAsync(
                    limit: _options.PageSize,
                    offset: offset,
                    closed: _options.IncludeClosed,
                    order: "id",
                    ascending: false,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (!result.IsSuccess)
            {
                throw new InvalidOperationException(
                    $"Gamma markets fetch failed: status={result.StatusCode} message={result.Error?.Message ?? "UNKNOWN"}");
            }

            var items = result.Data ?? Array.Empty<GammaMarket>();
            if (items.Count == 0)
            {
                break;
            }

            var pageMapped = new List<MarketInfo>(items.Count);
            foreach (var gm in items)
            {
                var mi = Map(gm);
                if (mi is not null)
                {
                    mapped.Add(mi);
                    pageMapped.Add(mi);
                }
            }

            if (pageMapped.Count > 0)
            {
                _catalog.UpdateMarkets(pageMapped);
            }

            total += items.Count;

            if (items.Count < _options.PageSize)
            {
                break;
            }
        }

        if (mapped.Count > 0)
        {
            // 持久化到数据库（best-effort：内存缓存仍可用）
            await PersistMarketsSafeAsync(mapped, cancellationToken).ConfigureAwait(false);
        }

        sw.Stop();
        Logger.LogInformation(
            "MarketCatalog refreshed: fetched={Fetched} mapped={Mapped} catalogCount={CatalogCount} durationMs={DurationMs}",
            total,
            mapped.Count,
            _catalog.Count,
            sw.ElapsedMilliseconds);
    }

    private async Task PersistMarketsSafeAsync(IReadOnlyList<MarketInfo> markets, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var repository = scope.ServiceProvider.GetRequiredService<IMarketRepository>();

            var now = DateTimeOffset.UtcNow;
            var dtos = markets.Select(m => new MarketDto(
                Id: Guid.NewGuid(),
                MarketId: m.MarketId,
                Name: m.Name,
                Status: m.Status,
                ExpiresAtUtc: m.ExpiresAtUtc,
                CreatedAtUtc: now,
                UpdatedAtUtc: now)).ToList();

            // 分批持久化，避免一次性构造超大 IN (...) / 参数列表导致性能/稳定性问题。
            for (var i = 0; i < dtos.Count; i += PersistBatchSize)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var batch = dtos.Skip(i).Take(PersistBatchSize);
                await repository.UpsertRangeAsync(batch, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // no-op
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to persist markets to database");
        }
    }

    private static MarketInfo? Map(GammaMarket market)
    {
        if (string.IsNullOrWhiteSpace(market.Id) || string.IsNullOrWhiteSpace(market.ConditionId))
        {
            return null;
        }

        var name = !string.IsNullOrWhiteSpace(market.Question) ? market.Question! : market.Id;

        var status = MarketStatus.Unknown;
        if (market.Closed == true)
        {
            status = MarketStatus.Closed;
        }
        else if (market.Active == true)
        {
            status = MarketStatus.Active;
        }
        else if (market.Active == false)
        {
            status = MarketStatus.Suspended;
        }

        DateTimeOffset? expiresAtUtc = null;
        if (!string.IsNullOrWhiteSpace(market.EndDateIso) &&
            DateTimeOffset.TryParse(market.EndDateIso, out var parsed))
        {
            expiresAtUtc = parsed.ToUniversalTime();
        }

        var tokenIds = ParseTokenIds(market.ClobTokenIds);

        var volume24h = market.Volume24hrClob
                       ?? market.Volume24hr
                       ?? market.VolumeNum
                       ?? 0m;

        var liquidity = market.LiquidityNum ?? 0m;

        return new MarketInfo
        {
            MarketId = market.Id.Trim(),
            ConditionId = market.ConditionId.Trim(),
            Name = name.Trim(),
            Category = string.IsNullOrWhiteSpace(market.Category) ? null : market.Category.Trim(),
            Slug = string.IsNullOrWhiteSpace(market.Slug) ? null : market.Slug.Trim(),
            Status = status,
            ExpiresAtUtc = expiresAtUtc,
            Volume24h = volume24h,
            Liquidity = liquidity,
            TokenIds = tokenIds
        };
    }

    private static IReadOnlyList<string> ParseTokenIds(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Array.Empty<string>();
        }

        try
        {
            var ids = JsonSerializer.Deserialize<List<string>>(json);
            if (ids is null || ids.Count == 0)
            {
                return Array.Empty<string>();
            }

            return ids
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }
}

