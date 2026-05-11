using System.Collections.Concurrent;
using Autotrade.Strategy.Application.Contract.Strategies;
using Autotrade.Strategy.Application.Observability;
using Autotrade.Trading.Application.Contract.Execution;
using Autotrade.Trading.Application.Contract.Risk;
using Autotrade.Trading.Domain.Shared.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Autotrade.Strategy.Application.Strategies.DualLeg;

/// <summary>
/// 双腿套利策略：同时买入 Yes 和 No 结果代币，利用二元市场的定价偏差套利。
/// 
/// 核心逻辑：
/// - 当 Yes 卖一价 + No 卖一价 < 阈值（如 0.98）时，同时买入两者
/// - 因为无论最终结果如何，其中一个代币必然价值 1.00
/// - 利润 = 1.00 - (Yes买入价 + No买入价) - 手续费
/// 
/// 风险控制：
/// - 必须同时成交两腿才能对冲，单腿敞口需要限制
/// - 超时未对冲触发 Kill Switch
/// </summary>
public sealed class DualLegArbitrageStrategy : TradingStrategyBase
{
    /// <summary>
    /// 配置监控器，支持运行时动态更新配置。
    /// </summary>
    private readonly IOptionsMonitor<DualLegArbitrageOptions> _optionsMonitor;

    /// <summary>
    /// 日志记录器。
    /// </summary>
    private readonly ILogger<DualLegArbitrageStrategy> _logger;

    /// <summary>
    /// 各市场的持仓状态追踪（线程安全）。
    /// Key: MarketId, Value: 该市场的持仓状态。
    /// </summary>
    private readonly ConcurrentDictionary<string, MarketPositionState> _positions = new();

    /// <summary>
    /// 市场 TokenId 缓存（用于对冲时获取正确的 tokenId）。
    /// Key: MarketId, Value: (YesTokenId, NoTokenId)
    /// </summary>
    private readonly ConcurrentDictionary<string, (string YesTokenId, string NoTokenId)> _marketTokenCache = new();

    /// <summary>
    /// 初始化双腿套利策略实例。
    /// </summary>
    /// <param name="context">策略上下文，包含执行服务、风险管理器、市场目录等依赖。</param>
    /// <param name="optionsMonitor">配置监控器。</param>
    /// <param name="logger">日志记录器。</param>
    public DualLegArbitrageStrategy(
        StrategyContext context,
        IOptionsMonitor<DualLegArbitrageOptions> optionsMonitor,
        ILogger<DualLegArbitrageStrategy> logger)
        : base(context, "DualLegArbitrage")
    {
        _optionsMonitor = optionsMonitor ?? throw new ArgumentNullException(nameof(optionsMonitor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 选择适合策略运行的市场。
    /// 
    /// 优先级（从高到低）：
    /// 1. 有未对冲敞口的市场（必须优先处理，避免风险！）
    /// 2. 快到期的市场（越接近到期，价格越趋于 0/1，套利机会更明确）
    /// 3. 高流动性市场
    /// 
    /// 筛选条件：
    /// - 流动性 >= MinLiquidity
    /// - 24小时交易量 >= MinVolume24h
    /// - 至少有两个 Token（Yes/No）
    /// - 距到期时间 >= MinTimeToExpiryMinutes
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>符合条件的市场 ID 列表。</returns>
    public override Task<IEnumerable<string>> SelectMarketsAsync(CancellationToken cancellationToken = default)
    {
        var options = _optionsMonitor.CurrentValue;
        options.Validate();

        // 策略未启用时返回空列表
        if (!options.Enabled)
        {
            return Task.FromResult<IEnumerable<string>>(Array.Empty<string>());
        }

        var now = DateTimeOffset.UtcNow;
        var minExpiryTime = now.AddMinutes(options.MinTimeToExpiryMinutes);

        // 【强制纳入】只要存在未对冲敞口，该市场必须被订阅（否则可能永远拿不到盘口无法对冲/退出）
        // 该强制集会绕过 MinLiquidity/MinVolume/MinTimeToExpiry/MaxMarkets 等筛选条件。
        var forcedMarkets = _positions
            .Where(p => p.Value.HasUnhedgedExposure)
            .Select(p => new
            {
                MarketId = p.Key,
                UnhedgedSinceUtc = p.Value.FirstLegFilledAtUtc,
                UnhedgedQuantity = p.Value.GetUnhedgedQuantity()
            })
            .OrderBy(p => p.UnhedgedSinceUtc ?? DateTimeOffset.MaxValue)             // 敞口更久的优先
            .ThenByDescending(p => p.UnhedgedQuantity)                               // 同时按数量降序
            .Select(p => p.MarketId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var forcedSet = forcedMarkets.ToHashSet(StringComparer.OrdinalIgnoreCase);

        // 常规筛选：只用于补齐“非风险强制”市场订阅
        var normalMarkets = Context.MarketCatalog.GetActiveMarkets()
            .Where(m => m.Liquidity >= options.MinLiquidity)                         // 流动性门槛
            .Where(m => m.Volume24h >= options.MinVolume24h)                         // 交易量门槛
            .Where(m => m.TokenIds.Count >= 2)                                       // 必须有 Yes/No 两个代币
            .Where(m => !m.ExpiresAtUtc.HasValue || m.ExpiresAtUtc.Value > minExpiryTime) // 到期时间过滤
            .Where(m => !forcedSet.Contains(m.MarketId))                             // 避免与强制集重复
            .OrderBy(m => m.ExpiresAtUtc ?? DateTimeOffset.MaxValue)                 // 快到期优先
            .ThenByDescending(m => m.Liquidity)                                      // 同到期按流动性降序
            .Select(m => m.MarketId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        var selected = new List<string>(capacity: Math.Max(options.MaxMarkets, forcedMarkets.Count));
        selected.AddRange(forcedMarkets);

        // 如果强制集已超过 MaxMarkets，仍然必须订阅全部强制市场（此时不再补齐常规市场）
        if (forcedMarkets.Count > options.MaxMarkets)
        {
            _logger.LogWarning(
                "Market selection: {Count} unhedged markets require monitoring, exceeding MaxMarkets={MaxMarkets}. " +
                "Subscribing to all unhedged markets and skipping normal selection. Examples: [{Markets}]",
                forcedMarkets.Count,
                options.MaxMarkets,
                string.Join(", ", forcedMarkets.Take(5)));
            return Task.FromResult<IEnumerable<string>>(selected);
        }

        foreach (var marketId in normalMarkets)
        {
            if (selected.Count >= options.MaxMarkets)
            {
                break;
            }

            selected.Add(marketId);
        }

        if (forcedMarkets.Count > 0)
        {
            _logger.LogDebug(
                "Market selection: {Count} unhedged markets forced-included. Examples: [{Markets}]",
                forcedMarkets.Count,
                string.Join(", ", forcedMarkets.Take(5)));
        }

        return Task.FromResult<IEnumerable<string>>(selected);
    }

    /// <summary>
    /// 评估入场信号：判断是否应该开仓。
    /// 
    /// 入场条件：
    /// 1. 策略启用且处于运行状态
    /// 2. 当前市场无未对冲敞口
    /// 3. 距离上次入场尝试已超过冷却时间
    /// 4. Yes 卖一价 + No 卖一价 < PairCostThreshold（套利空间存在）
    /// 5. 订单簿深度足够
    /// </summary>
    /// <param name="snapshot">市场快照，包含订单簿数据。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>入场信号（包含两腿订单），或 null 表示不入场。</returns>
    public override Task<StrategySignal?> EvaluateEntryAsync(MarketSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        var options = _optionsMonitor.CurrentValue;
        options.Validate();

        // 缓存市场的 tokenIds（用于对冲时获取正确的 tokenId）
        CacheMarketTokenIds(snapshot);

        // 基本前置条件检查
        if (!options.Enabled || State != StrategyState.Running)
        {
            return Task.FromResult<StrategySignal?>(null);
        }

        var position = GetPosition(snapshot.Market.MarketId);
        var now = DateTimeOffset.UtcNow;

        // 顺序模式：如存在未对冲敞口且第二腿未挂单，则尝试提交对冲腿
        if (options.SequentialOrderMode && position.HasUnhedgedExposure && !position.HasOpenOrder(OrderLeg.Second))
        {
            var secondLegSignal = CreateSecondLegSignal(snapshot, position, options);
            if (secondLegSignal is not null)
            {
                return Task.FromResult<StrategySignal?>(secondLegSignal);
            }
        }

        // 已有入场尝试且未完成对冲，不再重复下单（除非顺序模式第二腿）
        if (position.EntryStartedUtc.HasValue && !position.IsHedged)
        {
            return Task.FromResult<StrategySignal?>(null);
        }

        // 入场冷却时间检查，防止频繁交易
        if (position.LastEntryAttemptUtc.HasValue &&
            now - position.LastEntryAttemptUtc.Value < TimeSpan.FromSeconds(options.EntryCooldownSeconds))
        {
            return Task.FromResult<StrategySignal?>(null);
        }

        // 获取订单簿最优价
        var yesTop = snapshot.YesTopOfBook;
        var noTop = snapshot.NoTopOfBook;

        // 订单簿数据不完整，跳过
        if (yesTop?.BestAskPrice is null || noTop?.BestAskPrice is null)
        {
            return Task.FromResult<StrategySignal?>(null);
        }

        if (yesTop.BestAskSize is null || noTop.BestAskSize is null)
        {
            return Task.FromResult<StrategySignal?>(null);
        }

        if (options.MaxOrderBookAgeSeconds > 0)
        {
            var maxAge = TimeSpan.FromSeconds(options.MaxOrderBookAgeSeconds);
            if (snapshot.TimestampUtc - yesTop.LastUpdatedUtc > maxAge ||
                snapshot.TimestampUtc - noTop.LastUpdatedUtc > maxAge)
            {
                return Task.FromResult<StrategySignal?>(null);
            }
        }

        // 计算配对成本：同时买入 Yes 和 No 需要支付的总成本
        var yesPrice = yesTop.BestAskPrice.Value;
        var noPrice = noTop.BestAskPrice.Value;
        var pairCost = yesPrice + noPrice;

        // 价格必须在 Polymarket 允许的范围内 (0.01 ~ 0.99)
        const decimal MinPrice = 0.01m;
        const decimal MaxPrice = 0.99m;
        if ((yesPrice < MinPrice || noPrice < MinPrice) || (yesPrice > MaxPrice || noPrice > MaxPrice))
        {
            return Task.FromResult<StrategySignal?>(null);
        }

        // 配对成本 >= 阈值，无套利空间
        if (pairCost >= options.PairCostThreshold)
        {
            return Task.FromResult<StrategySignal?>(null);
        }

        // 检查每市场名义金额限制
        var remainingNotional = options.MaxNotionalPerMarket - position.OpenNotional;
        if (remainingNotional <= 0m)
        {
            _logger.LogDebug("Market {MarketId} reached MaxNotionalPerMarket limit", snapshot.Market.MarketId);
            return Task.FromResult<StrategySignal?>(null);
        }

        // 计算下单数量（取最小值）
        // 1. 按最大名义价值限制（单笔）
        var maxQtyByOrderNotional = Math.Min(
            options.MaxNotionalPerOrder / Math.Max(yesPrice, 0.01m),
            options.MaxNotionalPerOrder / Math.Max(noPrice, 0.01m));

        // 2. 按每市场剩余名义金额限制
        var maxQtyByMarketNotional = remainingNotional / Math.Max(yesPrice + noPrice, 0.02m);

        // 3. 按订单簿可用深度限制
        var availableQty = Math.Min(yesTop.BestAskSize.Value, noTop.BestAskSize.Value);

        // 4. 取配置默认值和所有限制的最小值
        var quantity = Math.Min(options.DefaultOrderQuantity,
            Math.Min(availableQty, Math.Min(maxQtyByOrderNotional, maxQtyByMarketNotional)));

        // 数量过小，不值得交易
        if (quantity < options.MinOrderQuantity)
        {
            return Task.FromResult<StrategySignal?>(null);
        }

        // 更新持仓状态
        position.LastEntryAttemptUtc = now;
        position.EntryStartedUtc ??= now;
        position.ExitSubmitted = false;

        // 获取代币 ID
        var yesTokenId = snapshot.Market.TokenIds[0];
        var noTokenId = snapshot.Market.TokenIds[1];

        // 计算限价（允许一定滑点）
        var yesLimit = Math.Min(0.99m, yesPrice * (1 + options.MaxSlippage));
        var noLimit = Math.Min(0.99m, noPrice * (1 + options.MaxSlippage));

        var isNegRisk = snapshot.Market.Slug?.Contains("neg", StringComparison.OrdinalIgnoreCase) == true;

        // 根据配置决定下单模式
        List<StrategyOrderIntent> orders;
        if (options.SequentialOrderMode)
        {
            // 顺序模式：只下第一腿，等待成交后再下第二腿
            // 第一腿选择价格较低的（更容易成交）
            var yesFirstLeg = yesPrice <= noPrice;
            var firstLegOutcome = yesFirstLeg ? OutcomeSide.Yes : OutcomeSide.No;

            orders = new List<StrategyOrderIntent>
            {
                new(
                    snapshot.Market.MarketId,
                    yesFirstLeg ? yesTokenId : noTokenId,
                    firstLegOutcome,
                    OrderSide.Buy,
                    OrderType.Limit,
                    TimeInForce.Gtc,
                    yesFirstLeg ? yesLimit : noLimit,
                    quantity,
                    isNegRisk,
                    OrderLeg.First)
            };

            _logger.LogDebug("Sequential entry: first leg={Outcome} for market {MarketId}",
                firstLegOutcome, snapshot.Market.MarketId);
        }
        else
        {
            // 同时下单模式（原行为）
            orders = new List<StrategyOrderIntent>
            {
                // 第一腿：买入 Yes
                new(
                    snapshot.Market.MarketId,
                    yesTokenId,
                    OutcomeSide.Yes,
                    OrderSide.Buy,
                    OrderType.Limit,
                    TimeInForce.Gtc,
                    yesLimit,
                    quantity,
                    isNegRisk,
                    OrderLeg.First),
                // 第二腿：买入 No
                new(
                    snapshot.Market.MarketId,
                    noTokenId,
                    OutcomeSide.No,
                    OrderSide.Buy,
                    OrderType.Limit,
                    TimeInForce.Gtc,
                    noLimit,
                    quantity,
                    isNegRisk,
                    OrderLeg.Second)
            };
        }

        // 生成入场信号
        var signal = new StrategySignal(
            StrategySignalType.Entry,
            snapshot.Market.MarketId,
            $"PairCost={pairCost:F4} < Threshold={options.PairCostThreshold:F4}",
            orders,
            $"{{\"pairCost\":{pairCost:F4},\"yesAsk\":{yesPrice:F4},\"noAsk\":{noPrice:F4},\"sequential\":{(options.SequentialOrderMode ? "true" : "false")}}}");

        return Task.FromResult<StrategySignal?>(signal);
    }

    /// <summary>
    /// 评估出场信号：判断是否应该平仓。
    /// 
    /// 出场条件（满足任一即触发）：
    /// 1. 配对价值 >= ExitPairValueThreshold（锁定利润）
    /// 2. 持有时间 >= MaxHoldSeconds（超时强制平仓）
    /// 
    /// 风险控制：
    /// - 单腿敞口超时触发 Kill Switch
    /// </summary>
    /// <param name="snapshot">市场快照，包含订单簿数据。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>出场信号（包含两腿订单），或 null 表示不出场。</returns>
    public override async Task<StrategySignal?> EvaluateExitAsync(MarketSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        var options = _optionsMonitor.CurrentValue;
        options.Validate();

        // 基本前置条件检查
        if (!options.Enabled || State != StrategyState.Running)
        {
            return null;
        }

        var position = GetPosition(snapshot.Market.MarketId);
        var now = DateTimeOffset.UtcNow;

        // 【风险控制】检测单腿敞口超时
        // 如果只有一腿成交，另一腿未成交超过阈值，执行配置的退出动作
        if (position.HasUnhedgedExposure && !position.TimeoutHandled)
        {
            var exposureStart = position.FirstLegFilledAtUtc ?? position.EntryStartedUtc;
            if (exposureStart.HasValue)
            {
                var exposureAge = now - exposureStart.Value;
                if (exposureAge > TimeSpan.FromSeconds(options.HedgeTimeoutSeconds))
                {
                    position.TimeoutHandled = true;
                    var exitSignal = await HandleHedgeTimeoutAsync(snapshot, position, options, cancellationToken)
                        .ConfigureAwait(false);

                    if (exitSignal is not null)
                    {
                        return exitSignal;
                    }
                }
            }
        }

        // 未完成对冲或已提交过出场订单，不再评估
        if (!position.IsHedged || position.ExitSubmitted)
        {
            return null;
        }

        // 获取订单簿最优买价
        var yesTop = snapshot.YesTopOfBook;
        var noTop = snapshot.NoTopOfBook;

        if (yesTop?.BestBidPrice is null || noTop?.BestBidPrice is null)
        {
            return null;
        }

        // 计算配对价值：同时卖出 Yes 和 No 可以获得的总价值
        var pairValue = yesTop.BestBidPrice.Value + noTop.BestBidPrice.Value;

        // 计算持有时间
        var holdDuration = position.EntryFilledUtc.HasValue
            ? now - position.EntryFilledUtc.Value
            : TimeSpan.Zero;

        // 未达到出场条件：配对价值未达阈值 且 未超时
        if (pairValue < options.ExitPairValueThreshold && holdDuration < TimeSpan.FromSeconds(options.MaxHoldSeconds))
        {
            return null;
        }

        // 计算可卖出数量（取两腿持仓的较小值）
        var exitQuantity = Math.Min(position.YesFilledQuantity, position.NoFilledQuantity);
        if (exitQuantity < options.MinOrderQuantity)
        {
            return null;
        }

        // 标记已提交出场订单，防止重复提交
        position.ExitSubmitted = true;

        // 获取代币 ID
        var yesTokenId = snapshot.Market.TokenIds[0];
        var noTokenId = snapshot.Market.TokenIds[1];

        // 计算限价（允许一定滑点）
        var yesLimit = Math.Max(0.01m, yesTop.BestBidPrice.Value * (1 - options.MaxSlippage));
        var noLimit = Math.Max(0.01m, noTop.BestBidPrice.Value * (1 - options.MaxSlippage));

        // 构造双腿卖出订单
        var orders = new List<StrategyOrderIntent>
        {
            // 第一腿：卖出 Yes
            new(
                snapshot.Market.MarketId,
                yesTokenId,
                OutcomeSide.Yes,
                OrderSide.Sell,
                OrderType.Limit,
                TimeInForce.Gtc,
                yesLimit,
                exitQuantity,
                snapshot.Market.Slug?.Contains("neg", StringComparison.OrdinalIgnoreCase) == true,
                OrderLeg.First),
            // 第二腿：卖出 No
            new(
                snapshot.Market.MarketId,
                noTokenId,
                OutcomeSide.No,
                OrderSide.Sell,
                OrderType.Limit,
                TimeInForce.Gtc,
                noLimit,
                exitQuantity,
                snapshot.Market.Slug?.Contains("neg", StringComparison.OrdinalIgnoreCase) == true,
                OrderLeg.Second)
        };

        // 生成出场信号
        var signal = new StrategySignal(
            StrategySignalType.Exit,
            snapshot.Market.MarketId,
            $"ExitPairValue={pairValue:F4}, HoldSeconds={holdDuration.TotalSeconds:F0}",
            orders,
            $"{{\"pairValue\":{pairValue:F4},\"yesBid\":{yesTop.BestBidPrice.Value:F4},\"noBid\":{noTop.BestBidPrice.Value:F4}}}");

        return signal;
    }

    /// <summary>
    /// 处理订单状态更新回调。
    /// 
    /// 主要职责：
    /// 1. 更新本地持仓状态（成交数量）
    /// 2. 检测对冲完成，清除敞口记录
    /// 3. 记录单腿敞口，用于风险监控
    /// 4. 处理订单取消/拒绝，重置状态以允许重试
    /// </summary>
    /// <param name="update">订单状态更新。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public override async Task OnOrderUpdateAsync(StrategyOrderUpdate update, CancellationToken cancellationToken = default)
    {
        var position = GetPosition(update.MarketId);

        if (update.SignalType == StrategySignalType.Entry)
        {
            position.EntryStartedUtc ??= update.TimestampUtc;
        }

        position.ApplyOrderUpdate(update);

        if (update.SignalType == StrategySignalType.Entry)
        {
            if (position.HasUnhedgedExposure)
            {
                position.FirstLegFilledAtUtc ??= update.TimestampUtc;
                position.TimeoutHandled = false;

                var outcome = position.GetUnhedgedOutcome();
                if (outcome.HasValue)
                {
                    var quantity = position.GetUnhedgedQuantity();
                    if (quantity > 0m)
                    {
                        var tokenId = outcome == OutcomeSide.Yes ? position.YesTokenId : position.NoTokenId;
                        var hedgeTokenId = outcome == OutcomeSide.Yes ? position.NoTokenId : position.YesTokenId;
                        var price = outcome == OutcomeSide.Yes ? position.LastYesFillPrice : position.LastNoFillPrice;

                        if (string.IsNullOrWhiteSpace(hedgeTokenId))
                        {
                            hedgeTokenId = GetCachedHedgeTokenId(update.MarketId, outcome.Value);
                        }

                        tokenId ??= update.TokenId;
                        hedgeTokenId ??= string.Empty;

                        if (string.IsNullOrWhiteSpace(tokenId) || string.IsNullOrWhiteSpace(hedgeTokenId))
                        {
                            _logger.LogWarning(
                                "Unhedged exposure skipped: missing token ids. Strategy={StrategyId}, Market={MarketId}, Token={TokenId}, HedgeToken={HedgeTokenId}",
                                Id,
                                update.MarketId,
                                tokenId ?? "(null)",
                                hedgeTokenId);
                        }
                        else
                        {
                            await Context.RiskManager.RecordUnhedgedExposureAsync(
                                Id,
                                update.MarketId,
                                tokenId,
                                hedgeTokenId,
                                outcome.Value,
                                OrderSide.Buy,
                                quantity,
                                price ?? update.Price,
                                position.FirstLegFilledAtUtc ?? update.TimestampUtc,
                                cancellationToken).ConfigureAwait(false);
                        }
                    }
                }
            }
            else if (position.IsHedged)
            {
                if (position.FirstLegFilledAtUtc.HasValue)
                {
                    var hedgeSeconds = (update.TimestampUtc - position.FirstLegFilledAtUtc.Value).TotalSeconds;
                    if (hedgeSeconds >= 0d)
                    {
                        StrategyMetrics.RecordHedgeTime(Id, hedgeSeconds);
                    }
                }

                position.EntryFilledUtc ??= update.TimestampUtc;
                position.FirstLegFilledAtUtc = null;
                position.TimeoutHandled = false;

                await Context.RiskManager.ClearUnhedgedExposureAsync(Id, update.MarketId, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        else if (update.SignalType == StrategySignalType.Exit)
        {
            if (position.IsFlat)
            {
                await Context.RiskManager.ClearUnhedgedExposureAsync(Id, update.MarketId, cancellationToken)
                    .ConfigureAwait(false);
                _positions.TryRemove(update.MarketId, out _);
            }
        }

        if (update.Status is ExecutionStatus.Cancelled or ExecutionStatus.Rejected or ExecutionStatus.Expired)
        {
            if (update.SignalType == StrategySignalType.Entry && position.IsFlat)
            {
                position.EntryStartedUtc = null;
                position.EntryFilledUtc = null;
                position.FirstLegFilledAtUtc = null;
                position.TimeoutHandled = false;
                position.ExitSubmitted = false;
            }
            else if (update.SignalType == StrategySignalType.Exit)
            {
                position.ExitSubmitted = false;
            }
        }
    }

    /// <summary>
    /// 获取或创建指定市场的持仓状态。
    /// </summary>
    /// <param name="marketId">市场 ID。</param>
    /// <returns>持仓状态对象。</returns>
    private MarketPositionState GetPosition(string marketId)
    {
        return _positions.GetOrAdd(marketId, id => new MarketPositionState(id));
    }

    private static bool IsOpenStatus(ExecutionStatus status)
        => status is ExecutionStatus.Pending or ExecutionStatus.Accepted or ExecutionStatus.PartiallyFilled;

    /// <summary>
    /// 处理对冲超时，根据配置执行相应的退出动作。
    /// </summary>
    private async Task<StrategySignal?> HandleHedgeTimeoutAsync(
        MarketSnapshot snapshot,
        MarketPositionState position,
        DualLegArbitrageOptions options,
        CancellationToken cancellationToken)
    {
        _logger.LogWarning("Hedge timeout for market {MarketId}, action={Action}",
            snapshot.Market.MarketId, options.HedgeTimeoutAction);

        if (options.HedgeTimeoutAction != UnhedgedExitAction.LogOnly)
        {
            await CancelOpenOrdersAsync(position, cancellationToken).ConfigureAwait(false);
        }

        switch (options.HedgeTimeoutAction)
        {
            case UnhedgedExitAction.LogOnly:
                // 仅记录，不执行任何动作
                return null;

            case UnhedgedExitAction.CancelOrders:
                // 取消挂单并软停止策略
                await Context.RiskManager.ActivateStrategyKillSwitchAsync(
                    Id,
                    KillSwitchLevel.SoftStop,
                    "HEDGE_TIMEOUT",
                    $"Hedge timeout for {snapshot.Market.MarketId}, cancelling orders",
                    snapshot.Market.MarketId,
                    null,
                    cancellationToken).ConfigureAwait(false);
                return null;

            case UnhedgedExitAction.CancelAndExit:
                // 取消挂单并以市价退出已成交的腿
                return await CreateExitSignalForUnhedgedLegAsync(snapshot, position, options)
                    .ConfigureAwait(false);

            case UnhedgedExitAction.ForceHedge:
                // 强制对冲：以激进价格买入第二腿
                return await CreateForceHedgeSignalAsync(snapshot, position, options)
                    .ConfigureAwait(false);

            default:
                _logger.LogWarning("Unknown hedge timeout action: {Action}", options.HedgeTimeoutAction);
                return null;
        }
    }

    private async Task CancelOpenOrdersAsync(MarketPositionState position, CancellationToken cancellationToken)
    {
        var openOrders = position.GetOpenOrderIds();
        if (openOrders.Count == 0)
        {
            return;
        }

        foreach (var clientOrderId in openOrders)
        {
            if (string.IsNullOrWhiteSpace(clientOrderId))
            {
                continue;
            }

            var result = await Context.ExecutionService.CancelOrderAsync(clientOrderId, cancellationToken)
                .ConfigureAwait(false);

            if (!result.Success)
            {
                _logger.LogWarning(
                    "Failed to cancel order {ClientOrderId} for market {MarketId}: {Error}",
                    clientOrderId,
                    position.MarketId,
                    result.ErrorMessage ?? result.ErrorCode ?? "UNKNOWN");
            }
        }
    }

    /// <summary>
    /// 为未对冲的腿创建退出信号（卖出已成交的腿）。
    /// </summary>
    private Task<StrategySignal?> CreateExitSignalForUnhedgedLegAsync(
        MarketSnapshot snapshot,
        MarketPositionState position,
        DualLegArbitrageOptions options)
    {
        var unhedgedOutcome = position.GetUnhedgedOutcome();
        if (unhedgedOutcome is null)
        {
            return Task.FromResult<StrategySignal?>(null);
        }

        var filledQty = position.GetUnhedgedQuantity();
        var outcome = unhedgedOutcome.Value;
        var tokenId = outcome == OutcomeSide.Yes ? position.YesTokenId : position.NoTokenId;
        var topOfBook = outcome == OutcomeSide.Yes ? snapshot.YesTopOfBook : snapshot.NoTopOfBook;

        if (filledQty < options.MinOrderQuantity || string.IsNullOrWhiteSpace(tokenId))
        {
            return Task.FromResult<StrategySignal?>(null);
        }

        // 以最低价快速卖出（市价退出）
        var exitPrice = topOfBook?.BestBidPrice?.Value ?? 0.01m;
        exitPrice = Math.Max(0.01m, exitPrice * (1 - options.MaxSlippage * 2)); // 更激进的滑点

        var order = new StrategyOrderIntent(
            snapshot.Market.MarketId,
            tokenId,
            outcome,
            OrderSide.Sell,
            OrderType.Limit,
            TimeInForce.Fok, // Fill-or-Kill 确保快速执行
            exitPrice,
            filledQty,
            snapshot.Market.Slug?.Contains("neg", StringComparison.OrdinalIgnoreCase) == true,
            outcome == OutcomeSide.Yes ? OrderLeg.First : OrderLeg.Second);

        var signal = new StrategySignal(
            StrategySignalType.Exit,
            snapshot.Market.MarketId,
            $"HedgeTimeout: exiting {outcome} leg at {exitPrice:F4}",
            new List<StrategyOrderIntent> { order },
            $"{{\"action\":\"CancelAndExit\",\"outcome\":\"{outcome}\",\"qty\":{filledQty:F4}}}");

        position.ExitSubmitted = true;

        return Task.FromResult<StrategySignal?>(signal);
    }

    /// <summary>
    /// 创建第二腿订单信号（顺序下单模式）。
    /// </summary>
    private StrategySignal? CreateSecondLegSignal(
        MarketSnapshot snapshot,
        MarketPositionState position,
        DualLegArbitrageOptions options)
    {
        var unhedgedOutcome = position.GetUnhedgedOutcome();
        if (unhedgedOutcome is null)
        {
            return null;
        }

        // 确定第二腿的方向（与未对冲腿相反）
        var secondLegOutcome = unhedgedOutcome == OutcomeSide.Yes ? OutcomeSide.No : OutcomeSide.Yes;
        var secondLegTokenId = secondLegOutcome == OutcomeSide.Yes
            ? snapshot.Market.TokenIds[0]
            : snapshot.Market.TokenIds[1];
        var secondLegTop = secondLegOutcome == OutcomeSide.Yes ? snapshot.YesTopOfBook : snapshot.NoTopOfBook;

        if (secondLegTop?.BestAskPrice is null || secondLegTop.BestAskSize is null)
        {
            _logger.LogWarning("Second leg order book not available: Market={MarketId}", snapshot.Market.MarketId);
            return null;
        }

        var quantity = position.GetUnhedgedQuantity();

        // 检查订单簿深度
        var maxQtyByOrderNotional = options.MaxNotionalPerOrder / Math.Max(secondLegTop.BestAskPrice.Value, 0.01m);
        quantity = Math.Min(quantity, maxQtyByOrderNotional);
        quantity = Math.Min(quantity, secondLegTop.BestAskSize.Value);

        if (quantity < options.MinOrderQuantity)
        {
            _logger.LogWarning(
                "Second leg quantity too small: Market={MarketId}, Qty={Qty}, MinQty={MinQty}, " +
                "UnhedgedQty={UnhedgedQty}, AskSize={AskSize}, MaxQtyByNotional={MaxQtyByNotional}",
                snapshot.Market.MarketId,
                quantity,
                options.MinOrderQuantity,
                position.GetUnhedgedQuantity(),
                secondLegTop.BestAskSize.Value,
                maxQtyByOrderNotional);
            return null;
        }

        var price = secondLegTop.BestAskPrice.Value;
        var limitPrice = Math.Min(0.99m, price * (1 + options.MaxSlippage));

        var order = new StrategyOrderIntent(
            snapshot.Market.MarketId,
            secondLegTokenId,
            secondLegOutcome,
            OrderSide.Buy,
            OrderType.Limit,
            TimeInForce.Gtc,
            limitPrice,
            quantity,
            snapshot.Market.Slug?.Contains("neg", StringComparison.OrdinalIgnoreCase) == true,
            OrderLeg.Second);

        _logger.LogDebug("Submitting second leg: Market={MarketId}, Outcome={Outcome}, Qty={Qty}, Price={Price}",
            snapshot.Market.MarketId, secondLegOutcome, quantity, limitPrice);

        return new StrategySignal(
            StrategySignalType.Entry,
            snapshot.Market.MarketId,
            $"Sequential second leg: {secondLegOutcome} at {limitPrice:F4}",
            new List<StrategyOrderIntent> { order },
            $"{{\"action\":\"SecondLeg\",\"outcome\":\"{secondLegOutcome}\",\"qty\":{quantity:F4},\"price\":{limitPrice:F4}}}");
    }

    /// <summary>
    /// 创建强制对冲信号（以激进价格买入第二腿）。
    /// </summary>
    private Task<StrategySignal?> CreateForceHedgeSignalAsync(
        MarketSnapshot snapshot,
        MarketPositionState position,
        DualLegArbitrageOptions options)
    {
        var unhedgedOutcome = position.GetUnhedgedOutcome();
        if (unhedgedOutcome is null)
        {
            return Task.FromResult<StrategySignal?>(null);
        }

        var outcome = unhedgedOutcome == OutcomeSide.Yes ? OutcomeSide.No : OutcomeSide.Yes;
        var tokenId = outcome == OutcomeSide.Yes ? snapshot.Market.TokenIds[0] : snapshot.Market.TokenIds[1];
        var quantity = position.GetUnhedgedQuantity();
        var topOfBook = outcome == OutcomeSide.Yes ? snapshot.YesTopOfBook : snapshot.NoTopOfBook;

        if (topOfBook?.BestAskPrice is null || topOfBook.BestAskSize is null)
        {
            return Task.FromResult<StrategySignal?>(null);
        }

        if (quantity < options.MinOrderQuantity)
        {
            return Task.FromResult<StrategySignal?>(null);
        }

        var maxQtyByOrderNotional = options.MaxNotionalPerOrder / Math.Max(topOfBook.BestAskPrice.Value, 0.01m);
        quantity = Math.Min(quantity, maxQtyByOrderNotional);
        quantity = Math.Min(quantity, topOfBook.BestAskSize.Value);

        if (quantity < options.MinOrderQuantity)
        {
            return Task.FromResult<StrategySignal?>(null);
        }

        // 以最高价快速买入（激进对冲）
        var hedgePrice = topOfBook.BestAskPrice.Value;
        hedgePrice = Math.Min(0.99m, hedgePrice * (1 + options.MaxSlippage * 2)); // 更激进的滑点

        var order = new StrategyOrderIntent(
            snapshot.Market.MarketId,
            tokenId,
            outcome,
            OrderSide.Buy,
            OrderType.Limit,
            TimeInForce.Fok, // Fill-or-Kill 确保快速执行
            hedgePrice,
            quantity,
            snapshot.Market.Slug?.Contains("neg", StringComparison.OrdinalIgnoreCase) == true,
            OrderLeg.Second);

        var signal = new StrategySignal(
            StrategySignalType.Entry, // 仍然是入场信号（完成对冲）
            snapshot.Market.MarketId,
            $"ForceHedge: buying {outcome} leg at {hedgePrice:F4}",
            new List<StrategyOrderIntent> { order },
            $"{{\"action\":\"ForceHedge\",\"outcome\":\"{outcome}\",\"qty\":{quantity:F4}}}");

        return Task.FromResult<StrategySignal?>(signal);
    }

    /// <summary>
    /// 缓存市场的 TokenIds，用于对冲时获取正确的 tokenId。
    /// Polymarket 市场通常有两个 token：TokenIds[0] = Yes, TokenIds[1] = No
    /// </summary>
    /// <param name="snapshot">市场快照。</param>
    private void CacheMarketTokenIds(MarketSnapshot snapshot)
    {
        var tokenIds = snapshot.Market.TokenIds;
        if (tokenIds.Count >= 2)
        {
            _marketTokenCache[snapshot.Market.MarketId] = (tokenIds[0], tokenIds[1]);
        }
    }

    /// <summary>
    /// 获取缓存的对冲腿 TokenId。
    /// </summary>
    /// <param name="marketId">市场 ID。</param>
    /// <param name="firstLegOutcome">第一腿的 outcome。</param>
    /// <returns>对冲腿的 tokenId，如果未缓存则返回 null。</returns>
    private string? GetCachedHedgeTokenId(string marketId, OutcomeSide firstLegOutcome)
    {
        if (_marketTokenCache.TryGetValue(marketId, out var tokens))
        {
            // 如果第一腿是 Yes，对冲腿是 No（tokens.NoTokenId）
            // 如果第一腿是 No，对冲腿是 Yes（tokens.YesTokenId）
            return firstLegOutcome == OutcomeSide.Yes ? tokens.NoTokenId : tokens.YesTokenId;
        }
        return null;
    }

    /// <summary>
    /// 单个市场的持仓状态内部类。
    /// 追踪入场/出场进度、双腿成交数量、对冲状态等。
    /// </summary>
    private sealed class MarketPositionState
    {
        private const decimal QuantityTolerance = 0.0001m;
        private readonly Dictionary<string, OrderTracking> _orders = new();

        public MarketPositionState(string marketId)
        {
            MarketId = marketId;
        }

        /// <summary>
        /// 市场 ID。
        /// </summary>
        public string MarketId { get; }

        /// <summary>
        /// 上次入场尝试时间（用于冷却时间控制）。
        /// </summary>
        public DateTimeOffset? LastEntryAttemptUtc { get; set; }

        /// <summary>
        /// 入场开始时间（首次提交入场订单）。
        /// </summary>
        public DateTimeOffset? EntryStartedUtc { get; set; }

        /// <summary>
        /// 入场完成时间（两腿都已成交）。
        /// </summary>
        public DateTimeOffset? EntryFilledUtc { get; set; }

        /// <summary>
        /// 第一腿成交时间（用于未对冲敞口计时）。
        /// </summary>
        public DateTimeOffset? FirstLegFilledAtUtc { get; set; }

        /// <summary>
        /// Yes 代币成交数量。
        /// </summary>
        public decimal YesFilledQuantity { get; private set; }

        /// <summary>
        /// No 代币成交数量。
        /// </summary>
        public decimal NoFilledQuantity { get; private set; }

        /// <summary>
        /// 已成交的名义金额（用于限制单市场资金占用）。
        /// </summary>
        public decimal OpenNotional { get; private set; }

        /// <summary>
        /// 最近一次 Yes 方向成交价。
        /// </summary>
        public decimal? LastYesFillPrice { get; private set; }

        /// <summary>
        /// 最近一次 No 方向成交价。
        /// </summary>
        public decimal? LastNoFillPrice { get; private set; }

        /// <summary>
        /// Yes 代币 Token ID（用于退出/对冲）。
        /// </summary>
        public string? YesTokenId { get; private set; }

        /// <summary>
        /// No 代币 Token ID（用于退出/对冲）。
        /// </summary>
        public string? NoTokenId { get; private set; }

        /// <summary>
        /// 是否已提交出场订单（防止重复提交）。
        /// </summary>
        public bool ExitSubmitted { get; set; }

        /// <summary>
        /// 是否已处理超时（防止重复处理）。
        /// </summary>
        public bool TimeoutHandled { get; set; }

        /// <summary>
        /// 是否已完成对冲（两腿都有成交）。
        /// 完成对冲后，无论最终结果如何，都有保底收益。
        /// </summary>
        public bool IsHedged =>
            YesFilledQuantity > QuantityTolerance &&
            NoFilledQuantity > QuantityTolerance &&
            Math.Abs(YesFilledQuantity - NoFilledQuantity) <= QuantityTolerance;

        /// <summary>
        /// 是否存在单腿敞口（只有一腿成交）。
        /// 单腿敞口有方向性风险，需要限制超时。
        /// </summary>
        public bool HasUnhedgedExposure =>
            Math.Abs(YesFilledQuantity - NoFilledQuantity) > QuantityTolerance;

        /// <summary>
        /// 是否已平仓（无持仓）。
        /// </summary>
        public bool IsFlat =>
            YesFilledQuantity <= QuantityTolerance && NoFilledQuantity <= QuantityTolerance;

        public void ApplyOrderUpdate(StrategyOrderUpdate update)
        {
            if (!_orders.TryGetValue(update.ClientOrderId, out var order))
            {
                order = new OrderTracking(update.Leg, update.Side, update.Outcome);
                _orders[update.ClientOrderId] = order;
            }

            var filled = Math.Max(order.FilledQuantity, update.FilledQuantity);
            var delta = filled - order.FilledQuantity;

            order.FilledQuantity = filled;
            order.Status = update.Status;
            order.Leg = update.Leg;
            order.Side = update.Side;
            order.Outcome = update.Outcome;

            if (update.Outcome == OutcomeSide.Yes)
            {
                YesTokenId ??= update.TokenId;
            }
            else
            {
                NoTokenId ??= update.TokenId;
            }

            if (delta <= 0m)
            {
                return;
            }

            var signedDelta = update.Side == OrderSide.Buy ? delta : -delta;

            if (update.Outcome == OutcomeSide.Yes)
            {
                YesFilledQuantity = Math.Max(0m, YesFilledQuantity + signedDelta);
                LastYesFillPrice = update.Price;
            }
            else
            {
                NoFilledQuantity = Math.Max(0m, NoFilledQuantity + signedDelta);
                LastNoFillPrice = update.Price;
            }

            var notionalDelta = delta * update.Price;
            OpenNotional = Math.Max(0m,
                OpenNotional + (update.Side == OrderSide.Buy ? notionalDelta : -notionalDelta));
        }

        public IReadOnlyList<string> GetOpenOrderIds()
            => _orders
                .Where(x => IsOpenStatus(x.Value.Status))
                .Select(x => x.Key)
                .ToList();

        public bool HasOpenOrder(OrderLeg leg)
            => _orders.Values.Any(x => x.Leg == leg && IsOpenStatus(x.Status));

        public OutcomeSide? GetUnhedgedOutcome()
        {
            var diff = YesFilledQuantity - NoFilledQuantity;
            if (Math.Abs(diff) <= QuantityTolerance)
            {
                return null;
            }

            return diff > 0m ? OutcomeSide.Yes : OutcomeSide.No;
        }

        public decimal GetUnhedgedQuantity()
            => Math.Abs(YesFilledQuantity - NoFilledQuantity);

        private sealed class OrderTracking
        {
            public OrderTracking(OrderLeg leg, OrderSide side, OutcomeSide outcome)
            {
                Leg = leg;
                Side = side;
                Outcome = outcome;
            }

            public OrderLeg Leg { get; set; }

            public OrderSide Side { get; set; }

            public OutcomeSide Outcome { get; set; }

            public ExecutionStatus Status { get; set; }

            public decimal FilledQuantity { get; set; }
        }
    }
}
