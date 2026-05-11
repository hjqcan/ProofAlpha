using System.Globalization;
using Autotrade.MarketData.Application.Contract.Catalog;
using Autotrade.MarketData.Application.Contract.OrderBook;
using Autotrade.Polymarket.Abstractions;
using Autotrade.Strategy.Application.Contract.ControlRoom;
using Autotrade.Strategy.Application.Contract.Strategies;
using Autotrade.Strategy.Application.Decisions;
using Autotrade.Trading.Application.Contract.Execution;
using Autotrade.Trading.Application.Contract.Repositories;
using Autotrade.Trading.Application.Contract.Risk;
using Autotrade.Trading.Domain.Shared.Enums;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Autotrade.Api.ControlRoom;

public sealed class ControlRoomQueryService(
    IServiceProvider serviceProvider,
    IMemoryCache cache,
    IHostEnvironment environment,
    HealthCheckService healthCheckService,
    IControlRoomMarketDataService marketDataService,
    IOptionsMonitor<ControlRoomOptions> controlRoomOptions,
    IOptionsMonitor<ExecutionOptions> executionOptions,
    IOptionsMonitor<RiskOptions> riskOptions) : IControlRoomQueryService
{
    public async Task<ControlRoomSnapshotResponse> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var process = await BuildProcessAsync(cancellationToken).ConfigureAwait(false);
        var strategies = await BuildStrategiesAsync(cancellationToken).ConfigureAwait(false);
        var marketQuery = new ControlRoomMarketDiscoveryQuery(
            Status: "Active",
            Sort: "signal",
            Limit: controlRoomOptions.CurrentValue.MarketLimit,
            Offset: 0);
        var marketsResponse = await marketDataService
            .GetMarketsAsync(marketQuery, cancellationToken)
            .ConfigureAwait(false);
        var markets = marketsResponse.Markets;
        var orders = await BuildOrdersAsync(cancellationToken).ConfigureAwait(false);
        var positions = await BuildPositionsAsync(includeLiveFallbackMarks: false, cancellationToken).ConfigureAwait(false);
        var decisions = await BuildDecisionsAsync(now, cancellationToken).ConfigureAwait(false);
        var risk = BuildRisk(orders, positions);

        return new ControlRoomSnapshotResponse(
            now,
            controlRoomOptions.CurrentValue.DataMode,
            controlRoomOptions.CurrentValue.EffectiveCommandMode,
            process,
            risk,
            BuildMetrics(risk, strategies, markets, positions, decisions),
            strategies,
            markets,
            orders,
            positions,
            decisions,
            BuildTimeline(now, process, risk, strategies, decisions),
            Array.Empty<ControlRoomSeriesPointDto>(),
            Array.Empty<ControlRoomSeriesPointDto>());
    }

    private async Task<ControlRoomProcessDto> BuildProcessAsync(CancellationToken cancellationToken)
    {
        var report = await healthCheckService.CheckHealthAsync(cancellationToken).ConfigureAwait(false);
        var readyChecks = report.Entries.Count(entry => entry.Value.Status == HealthStatus.Healthy);
        var degradedChecks = report.Entries.Count(entry => entry.Value.Status == HealthStatus.Degraded);
        var unhealthyChecks = report.Entries.Count(entry => entry.Value.Status == HealthStatus.Unhealthy);

        return new ControlRoomProcessDto(
            report.Status.ToString(),
            environment.EnvironmentName,
            executionOptions.CurrentValue.Mode.ToString(),
            serviceProvider.GetService<IMarketCatalogReader>() is not null,
            readyChecks,
            degradedChecks,
            unhealthyChecks);
    }

    private async Task<IReadOnlyList<ControlRoomStrategyDto>> BuildStrategiesAsync(CancellationToken cancellationToken)
    {
        var provider = serviceProvider.GetService<IStrategyControlRoomReadModelProvider>();
        if (provider is null)
        {
            return Array.Empty<ControlRoomStrategyDto>();
        }

        var readModel = await provider.GetReadModelAsync(cancellationToken).ConfigureAwait(false);
        return readModel.Strategies
            .Select(ToStrategyDto)
            .ToArray();
    }

    private static ControlRoomStrategyDto ToStrategyDto(StrategyControlRoomCard card)
    {
        return new ControlRoomStrategyDto(
            card.StrategyId,
            card.Name,
            card.State,
            card.Enabled,
            card.ConfigVersion,
            card.DesiredState,
            card.ActiveMarkets,
            card.CycleCount,
            card.SnapshotsProcessed,
            card.ChannelBacklog,
            card.IsKillSwitchBlocked,
            card.LastHeartbeatUtc,
            card.LastDecisionAtUtc,
            card.LastError,
            ToBlockedReasonDto(card.BlockedReason),
            card.Parameters
                .Select(parameter => new ControlRoomParameterDto(parameter.Name, parameter.Value))
                .ToArray())
        {
            ModelVersion = card.ModelVersion,
            SourceVersion = card.SourceVersion
        };
    }

    private static ControlRoomStrategyBlockedReasonDto? ToBlockedReasonDto(StrategyBlockedReason? reason)
        => reason is null
            ? null
            : new ControlRoomStrategyBlockedReasonDto(
                reason.Kind.ToString(),
                reason.Code,
                reason.Message);

    private async Task<IReadOnlyList<ControlRoomOrderDto>> BuildOrdersAsync(CancellationToken cancellationToken)
    {
        var repository = serviceProvider.GetService<IOrderRepository>();
        if (repository is not null)
        {
            var openOrders = await repository.GetOpenOrdersAsync(cancellationToken).ConfigureAwait(false);
            var mapped = openOrders
                .OrderByDescending(item => item.UpdatedAtUtc)
                .Take(12)
                .Select(item => new ControlRoomOrderDto(
                    item.ClientOrderId ?? item.Id.ToString("N"),
                    item.StrategyId ?? "manual",
                    item.MarketId,
                    item.Side.ToString(),
                    item.Outcome.ToString(),
                    item.Price,
                    item.Quantity,
                    item.FilledQuantity,
                    item.Status.ToString(),
                    item.UpdatedAtUtc))
                .ToArray();

            return mapped;
        }

        return Array.Empty<ControlRoomOrderDto>();
    }

    private async Task<IReadOnlyList<ControlRoomPositionDto>> BuildPositionsAsync(
        bool includeLiveFallbackMarks,
        CancellationToken cancellationToken)
    {
        var repository = serviceProvider.GetService<IPositionRepository>();
        if (repository is not null)
        {
            var catalogReader = serviceProvider.GetService<IMarketCatalogReader>();
            var orderBookReader = serviceProvider.GetService<IOrderBookReader>();
            var positions = (await repository.GetNonZeroAsync(cancellationToken).ConfigureAwait(false)).ToArray();
            var tokenOverrides = await BuildPositionTokenOverridesAsync(
                    positions,
                    catalogReader,
                    cancellationToken)
                .ConfigureAwait(false);
            var fallbackMarks = includeLiveFallbackMarks
                ? await BuildFallbackPositionMarksAsync(
                        positions,
                        catalogReader,
                        tokenOverrides,
                        orderBookReader,
                        cancellationToken)
                    .ConfigureAwait(false)
                : new Dictionary<string, PositionExitMark>(StringComparer.Ordinal);

            var mapped = positions
                .OrderByDescending(item => item.Notional)
                .Select(item => ControlRoomPositionMapper.Map(
                    item,
                    catalogReader,
                    orderBookReader,
                    fallbackMarks,
                    tokenOverrides))
                .ToArray();

            return mapped;
        }

        return Array.Empty<ControlRoomPositionDto>();
    }

    private async Task<IReadOnlyDictionary<string, PositionExitMark>> BuildFallbackPositionMarksAsync(
        IReadOnlyCollection<PositionDto> positions,
        IMarketCatalogReader? catalogReader,
        IReadOnlyDictionary<string, string> tokenOverrides,
        IOrderBookReader? orderBookReader,
        CancellationToken cancellationToken)
    {
        if (positions.Count == 0 || !controlRoomOptions.CurrentValue.EnablePublicMarketData)
        {
            return new Dictionary<string, PositionExitMark>(StringComparer.Ordinal);
        }

        var clobClient = serviceProvider.GetService<IPolymarketClobClient>();
        if (clobClient is null)
        {
            return new Dictionary<string, PositionExitMark>(StringComparer.Ordinal);
        }

        var tokenIds = positions
            .Select(position => ControlRoomPositionMapper.ResolveTokenId(
                position,
                catalogReader?.GetMarket(position.MarketId),
                tokenOverrides))
            .Where(tokenId => !string.IsNullOrWhiteSpace(tokenId))
            .Select(tokenId => tokenId!)
            .Distinct(StringComparer.Ordinal)
            .Where(tokenId => LooksLikePolymarketToken(tokenId) &&
                orderBookReader?.GetTopOfBook(tokenId)?.BestBidPrice is null)
            .ToArray();

        if (tokenIds.Length == 0)
        {
            return new Dictionary<string, PositionExitMark>(StringComparer.Ordinal);
        }

        using var gate = new SemaphoreSlim(3, 3);
        var resolved = await Task
            .WhenAll(tokenIds.Select(tokenId => LoadFallbackPositionMarkAsync(tokenId, clobClient, gate, cancellationToken)))
            .ConfigureAwait(false);

        return resolved
            .Where(item => item.Mark.Price.HasValue)
            .ToDictionary(item => item.TokenId, item => item.Mark, StringComparer.Ordinal);
    }

    private async Task<(string TokenId, PositionExitMark Mark)> LoadFallbackPositionMarkAsync(
        string tokenId,
        IPolymarketClobClient clobClient,
        SemaphoreSlim gate,
        CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var cacheKey = $"control-room:position-mark:{tokenId}";
            var mark = await cache.GetOrCreateAsync(
                    cacheKey,
                    async entry =>
                    {
                        entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(
                            Math.Clamp(controlRoomOptions.CurrentValue.OrderBookCacheTtlSeconds * 6, 15, 120));

                        try
                        {
                            var result = await clobClient.GetOrderBookAsync(tokenId, cancellationToken).ConfigureAwait(false);
                            if (!result.IsSuccess || result.Data is null)
                            {
                                return new PositionExitMark(null, "Unavailable");
                            }

                            var bestBid = result.Data.Bids
                                .Select(level => ParseDecimal(level.Price))
                                .Where(price => price > 0m)
                                .DefaultIfEmpty(0m)
                                .Max();

                            return bestBid > 0m
                                ? new PositionExitMark(bestBid, "LiveClobBestBid")
                                : new PositionExitMark(0m, "NoBidZeroMark");
                        }
                        catch (Exception) when (!cancellationToken.IsCancellationRequested)
                        {
                            return new PositionExitMark(null, "Unavailable");
                        }
                    })
                .ConfigureAwait(false);

            return (tokenId, mark ?? new PositionExitMark(null, "Unavailable"));
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<IReadOnlyDictionary<string, string>> BuildPositionTokenOverridesAsync(
        IReadOnlyCollection<PositionDto> positions,
        IMarketCatalogReader? catalogReader,
        CancellationToken cancellationToken)
    {
        var orderRepository = serviceProvider.GetService<IOrderRepository>();
        if (positions.Count == 0 || orderRepository is null)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        var positionsMissingCatalogToken = positions
            .Where(position => string.IsNullOrWhiteSpace(
                ControlRoomPositionMapper.ResolveTokenId(position, catalogReader?.GetMarket(position.MarketId))))
            .GroupBy(position => position.MarketId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (positionsMissingCatalogToken.Length == 0)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        var tokenOverrides = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var marketGroup in positionsMissingCatalogToken)
        {
            var orders = await orderRepository
                .GetByMarketIdAsync(marketGroup.Key, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            foreach (var position in marketGroup)
            {
                var tokenId = orders
                    .Where(order => order.Outcome == position.Outcome && !string.IsNullOrWhiteSpace(order.TokenId))
                    .OrderByDescending(order => order.UpdatedAtUtc)
                    .Select(order => order.TokenId)
                    .FirstOrDefault();

                if (!string.IsNullOrWhiteSpace(tokenId))
                {
                    tokenOverrides[ControlRoomPositionMapper.BuildPositionKey(position.MarketId, position.Outcome)] =
                        tokenId;
                }
            }
        }

        return tokenOverrides;
    }

    private async Task<IReadOnlyList<ControlRoomDecisionDto>> BuildDecisionsAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var queryService = serviceProvider.GetService<IStrategyDecisionQueryService>();
        if (queryService is not null)
        {
            var decisions = await queryService.QueryAsync(
                new StrategyDecisionQuery(null, null, now.AddHours(-6), now, 8),
                cancellationToken).ConfigureAwait(false);

            var mapped = decisions
                .OrderByDescending(item => item.TimestampUtc)
                .Select(item => new ControlRoomDecisionDto(
                    item.StrategyId,
                    item.Action,
                    item.MarketId ?? "global",
                    item.Reason,
                    item.TimestampUtc))
                .ToArray();

            return mapped;
        }

        return Array.Empty<ControlRoomDecisionDto>();
    }

    private ControlRoomRiskDto BuildRisk(
        IReadOnlyList<ControlRoomOrderDto> orders,
        IReadOnlyList<ControlRoomPositionDto> positions)
    {
        var riskManager = serviceProvider.GetService<IRiskManager>();

        if (riskManager is not null)
        {
            var state = riskManager.GetStateSnapshot();
            var killSwitch = riskManager.GetKillSwitchState();
            var liveCapital = state.TotalCapital <= 0m ? controlRoomOptions.CurrentValue.PaperCapital : state.TotalCapital;
            var usage = ControlRoomRiskUsage.From(
                liveCapital,
                state.AvailableCapital,
                state.TotalOpenNotional,
                state.TotalOpenOrders,
                positions);

            return new ControlRoomRiskDto(
                killSwitch.IsActive,
                killSwitch.Level.ToString(),
                killSwitch.Reason,
                killSwitch.ActivatedAtUtc,
                liveCapital,
                usage.AvailableCapital,
                usage.CapitalUtilizationPct,
                usage.OpenNotional,
                usage.OpenOrders,
                state.UnhedgedExposures.Count,
                BuildRiskLimits(usage.OpenNotional, usage.OpenOrders, usage.CapitalUtilizationPct, liveCapital));
        }

        var openOrderNotional = orders
            .Where(IsOpenOrder)
            .Sum(RemainingOrderNotional);
        var openOrders = orders.Count(IsOpenOrder);
        var capital = controlRoomOptions.CurrentValue.PaperCapital;
        var fallbackUsage = ControlRoomRiskUsage.From(
            capital,
            capital,
            openOrderNotional,
            openOrders,
            positions);

        return new ControlRoomRiskDto(
            false,
            "None",
            null,
            null,
            capital,
            fallbackUsage.AvailableCapital,
            fallbackUsage.CapitalUtilizationPct,
            fallbackUsage.OpenNotional,
            openOrders,
            0,
            BuildRiskLimits(fallbackUsage.OpenNotional, openOrders, fallbackUsage.CapitalUtilizationPct, capital));
    }

    private IReadOnlyList<ControlRoomRiskLimitDto> BuildRiskLimits(
        decimal openNotional,
        int openOrders,
        decimal utilizationPct,
        decimal totalCapital)
    {
        var risk = riskOptions.CurrentValue;
        var maxUtilizationPct = risk.MaxTotalCapitalUtilization * 100m;
        var maxOpenNotional = totalCapital * risk.MaxTotalCapitalUtilization;

        return
        [
            new ControlRoomRiskLimitDto("Capital", utilizationPct, maxUtilizationPct, "%", LimitState(utilizationPct, maxUtilizationPct)),
            new ControlRoomRiskLimitDto("Open notional", openNotional, maxOpenNotional, "USDC", LimitState(openNotional, maxOpenNotional)),
            new ControlRoomRiskLimitDto("Open orders", openOrders, risk.MaxOpenOrders, "orders", LimitState(openOrders, risk.MaxOpenOrders))
        ];
    }

    private IReadOnlyList<ControlRoomMetricDto> BuildMetrics(
        ControlRoomRiskDto risk,
        IReadOnlyList<ControlRoomStrategyDto> strategies,
        IReadOnlyList<ControlRoomMarketDto> markets,
        IReadOnlyList<ControlRoomPositionDto> positions,
        IReadOnlyList<ControlRoomDecisionDto> decisions)
    {
        var running = strategies.Count(item => item.State == StrategyState.Running);
        var marketLiquidity = markets.Sum(item => item.Liquidity);
        var lastDecision = decisions.MaxBy(item => item.CreatedAtUtc)?.CreatedAtUtc;
        var lastDecisionAge = lastDecision is null
            ? "none"
            : $"{Math.Max(0, (int)(DateTimeOffset.UtcNow - lastDecision.Value).TotalSeconds)}s";
        var markedPositions = positions.Count(position => position.TotalPnl.HasValue);
        var paperPnl = positions.Sum(position => position.TotalPnl ?? position.RealizedPnl);
        var paperPnlTone = paperPnl switch
        {
            > 0m => "good",
            < 0m => "watch",
            _ => "neutral"
        };

        return
        [
            new ControlRoomMetricDto("Running strategies", running.ToString("0"), $"{strategies.Count} registered", running > 0 ? "good" : "muted"),
            new ControlRoomMetricDto("Open notional", $"{risk.OpenNotional:0.00}", BuildExposureDelta(positions.Count, risk.OpenOrders), risk.OpenNotional > 0m ? "watch" : "good"),
            new ControlRoomMetricDto("Available capital", $"{risk.AvailableCapital:0.00}", $"{risk.CapitalUtilizationPct:0.0}% utilized", risk.CapitalUtilizationPct > 35m ? "watch" : "good"),
            new ControlRoomMetricDto("Paper PnL", $"{paperPnl:+0.00;-0.00;0.00}", $"{markedPositions}/{positions.Count} marked", paperPnlTone),
            new ControlRoomMetricDto("Market liquidity", $"{marketLiquidity / 1000m:0.0}k", $"{markets.Count} watched", "neutral"),
            new ControlRoomMetricDto("Last decision", lastDecisionAge, "decision clock", "neutral")
        ];
    }

    private IReadOnlyList<ControlRoomTimelineItemDto> BuildTimeline(
        DateTimeOffset now,
        ControlRoomProcessDto process,
        ControlRoomRiskDto risk,
        IReadOnlyList<ControlRoomStrategyDto> strategies,
        IReadOnlyList<ControlRoomDecisionDto> decisions)
    {
        var items = new List<ControlRoomTimelineItemDto>
        {
            new(now.AddSeconds(-5), "API", $"Health status {process.ApiStatus}.", process.ApiStatus == "Healthy" ? "good" : "watch")
        };

        if (risk.KillSwitchActive)
        {
            items.Add(new ControlRoomTimelineItemDto(
                risk.KillSwitchActivatedAtUtc ?? now,
                "Kill switch",
                $"{risk.KillSwitchLevel}: {risk.KillSwitchReason}",
                "danger"));
        }

        items.AddRange(strategies
            .Where(item => item.LastHeartbeatUtc is not null)
            .Take(3)
            .Select(item => new ControlRoomTimelineItemDto(
                item.LastHeartbeatUtc!.Value,
                item.Name,
                $"Heartbeat accepted with backlog {item.ChannelBacklog}.",
                item.ChannelBacklog > 5 ? "watch" : "good")));

        items.AddRange(decisions.Take(4).Select(item => new ControlRoomTimelineItemDto(
            item.CreatedAtUtc,
            item.Action,
            $"{item.StrategyId} on {item.MarketId}.",
            "neutral")));

        return items
            .OrderByDescending(item => item.TimestampUtc)
            .Take(8)
            .ToArray();
    }

    private static string LimitState(decimal current, decimal limit)
    {
        if (limit <= 0m)
        {
            return "unknown";
        }

        var ratio = current / limit;
        return ratio switch
        {
            >= 0.90m => "danger",
            >= 0.70m => "watch",
            _ => "good"
        };
    }

    private static bool IsOpenOrder(ControlRoomOrderDto order)
        => order.Status is "Open" or "Pending" or "PartiallyFilled";

    private static decimal RemainingOrderNotional(ControlRoomOrderDto order)
        => order.Price * Math.Max(0m, order.Quantity - order.FilledQuantity);

    private static decimal ParseDecimal(string? raw)
    {
        return decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? value
            : 0m;
    }

    private static bool LooksLikePolymarketToken(string tokenId)
    {
        return tokenId.Length > 20 && tokenId.All(char.IsDigit);
    }

    private static string BuildExposureDelta(int positionCount, int openOrders)
    {
        var positionLabel = positionCount == 1 ? "position" : "positions";
        var orderLabel = openOrders == 1 ? "open order" : "open orders";

        return $"{positionCount} {positionLabel} / {openOrders} {orderLabel}";
    }
}

internal sealed record ControlRoomRiskUsage(
    decimal OpenNotional,
    int OpenOrders,
    decimal AvailableCapital,
    decimal CapitalUtilizationPct)
{
    public static ControlRoomRiskUsage From(
        decimal totalCapital,
        decimal availableCapital,
        decimal openOrderNotional,
        int openOrders,
        IReadOnlyList<ControlRoomPositionDto> positions)
    {
        var normalizedCapital = Math.Max(0m, totalCapital);
        var normalizedAvailableCapital = availableCapital <= 0m
            ? normalizedCapital
            : Math.Min(availableCapital, normalizedCapital);
        var positionNotional = positions.Sum(position => Math.Abs(position.Notional));
        var openNotional = Math.Max(0m, openOrderNotional) + positionNotional;
        var remainingCapital = Math.Max(0m, normalizedAvailableCapital - openNotional);
        var utilization = normalizedCapital <= 0m
            ? 0m
            : Math.Round(openNotional / normalizedCapital * 100m, 2);

        return new ControlRoomRiskUsage(
            openNotional,
            openOrders,
            remainingCapital,
            utilization);
    }
}
