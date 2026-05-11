// ============================================================================
// 尾盘扫货策略
// ============================================================================
// 在市场即将结算时，买入高胜率的一方以锁定小但几乎确定的利润。
// 
// 适用场景：二元预测市场（Polymarket）
// 核心思路：当某一方价格 >= 90% 时，该方向胜率极高
//          以略低于当前价的限价单买入，等待结算获得 1.00 兑付
// ============================================================================

using System.Collections.Concurrent;
using Autotrade.Strategy.Application.Contract.Strategies;
using Autotrade.Trading.Application.Contract.Execution;
using Autotrade.Trading.Application.Contract.Risk;
using Autotrade.Trading.Domain.Shared.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Autotrade.Strategy.Application.Strategies.Endgame;

/// <summary>
/// 尾盘扫货策略：在市场即将结算时，买入高胜率的一方以锁定小但几乎确定的利润。
/// 
/// 核心逻辑：
/// - 筛选即将结算（如 15 分钟内）的二元市场
/// - 当某一方价格 >= 阈值（如 0.90）时，该方向胜率极高
/// - 以略低于当前价格的限价单买入，等待结算获得 1.00 兑付
/// - 利润 = 1.00 - 买入价 - 手续费
/// 
/// 风险控制：
/// - 距离结算太近（如 < 1 分钟）可能无法成交
/// - 价格过高（如 > 0.98）利润过薄，手续费可能吞噬收益
/// - 极端情况下可能出现翻盘（虽然概率很低）
/// </summary>
public sealed class EndgameSweepStrategy : TradingStrategyBase
{
    /// <summary>
    /// 配置监控器，支持运行时动态更新。
    /// </summary>
    private readonly IOptionsMonitor<EndgameSweepOptions> _optionsMonitor;

    /// <summary>
    /// 日志记录器。
    /// </summary>
    private readonly ILogger<EndgameSweepStrategy> _logger;

    /// <summary>
    /// 各市场的持仓状态追踪（线程安全）。
    /// </summary>
    private readonly ConcurrentDictionary<string, MarketState> _marketStates = new();

    /// <summary>
    /// 初始化尾盘扫货策略实例。
    /// </summary>
    /// <param name="context">策略上下文。</param>
    /// <param name="optionsMonitor">配置监控器。</param>
    /// <param name="logger">日志记录器。</param>
    public EndgameSweepStrategy(
        StrategyContext context,
        IOptionsMonitor<EndgameSweepOptions> optionsMonitor,
        ILogger<EndgameSweepStrategy> logger)
        : base(context, "EndgameSweep")
    {
        _optionsMonitor = optionsMonitor ?? throw new ArgumentNullException(nameof(optionsMonitor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 选择适合策略运行的市场。
    /// 
    /// 优先级（从高到低）：
    /// 1. 已有持仓的市场（必须监控直到结算！）
    /// 2. 按到期时间升序（最快结算的优先）
    /// 
    /// 筛选条件：
    /// - 即将结算（在 MaxSecondsToExpiry 范围内）
    /// - 流动性 >= MinLiquidity
    /// - 至少有两个 Token（Yes/No）
    /// </summary>
    public override Task<IEnumerable<string>> SelectMarketsAsync(CancellationToken cancellationToken = default)
    {
        var options = _optionsMonitor.CurrentValue;
        options.Validate();

        if (!options.Enabled)
        {
            return Task.FromResult<IEnumerable<string>>(Array.Empty<string>());
        }

        var now = DateTimeOffset.UtcNow;
        var maxExpiry = TimeSpan.FromSeconds(options.MaxSecondsToExpiry);
        var minExpiry = TimeSpan.FromSeconds(options.MinSecondsToExpiry);

        // 【强制纳入】只要已有持仓，该市场必须被订阅直到结算（否则可能错过关键盘口/结算前异常波动）
        // 该强制集会绕过“到期窗口/MaxMarkets”等筛选条件。
        var forcedMarkets = _marketStates
            .Where(s => s.Value.HasPosition)
            .Select(s => s.Key)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(id =>
            {
                var market = Context.MarketCatalog.GetMarket(id);
                return new
                {
                    MarketId = id,
                    ExpiresAtUtc = market?.ExpiresAtUtc
                };
            })
            .OrderBy(x => x.ExpiresAtUtc ?? DateTimeOffset.MaxValue) // 先结算的优先
            .Select(x => x.MarketId)
            .ToList();

        var forcedSet = forcedMarkets.ToHashSet(StringComparer.OrdinalIgnoreCase);

        // 使用 GetExpiringMarkets 获取即将到期的市场
        var expiringMarkets = Context.MarketCatalog.GetExpiringMarkets(maxExpiry);

        var normalMarkets = expiringMarkets
            .Where(m => m.ExpiresAtUtc.HasValue)
            .Where(m => m.ExpiresAtUtc!.Value - now >= minExpiry)     // 至少还有最小时间
            .Where(m => string.Equals(m.Status, "Active", StringComparison.OrdinalIgnoreCase))
            .Where(m => m.Liquidity >= options.MinLiquidity)          // 流动性门槛
            .Where(m => m.TokenIds.Count >= 2)                        // 必须有 Yes/No 两个代币
            .Where(m => !forcedSet.Contains(m.MarketId))              // 避免与强制集重复
            .OrderBy(m => m.ExpiresAtUtc)                             // 快速结算优先
            .Select(m => m.MarketId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        var selected = new List<string>(capacity: Math.Max(options.MaxMarkets, forcedMarkets.Count));
        selected.AddRange(forcedMarkets);

        // 如果强制集已超过 MaxMarkets，仍然必须订阅全部强制市场（此时不再补齐常规市场）
        if (forcedMarkets.Count > options.MaxMarkets)
        {
            _logger.LogWarning(
                "[Endgame] Market selection: {Count} position markets require monitoring, exceeding MaxMarkets={MaxMarkets}. " +
                "Subscribing to all position markets and skipping normal selection. Examples: [{Markets}]",
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

        if (selected.Count > 0)
        {
            if (forcedMarkets.Count > 0)
            {
                _logger.LogDebug(
                    "[Endgame] 筛选到 {Count} 个即将结算的市场，其中 {PositionCount} 个有持仓",
                    selected.Count,
                    forcedMarkets.Count);
            }
            else
            {
                _logger.LogDebug("[Endgame] 筛选到 {Count} 个即将结算的市场", selected.Count);
            }
        }

        return Task.FromResult<IEnumerable<string>>(selected);
    }

    /// <summary>
    /// 评估入场信号：判断是否应该买入高胜率的一方。
    /// 
    /// 入场条件：
    /// 1. 策略启用且处于运行状态
    /// 2. 市场即将结算（在配置的时间窗口内）
    /// 3. Yes 或 No 的卖一价 >= MinWinProbability（高胜率信号）
    /// 4. 卖一价 <= MaxEntryPrice（避免利润过薄）
    /// 5. 预期收益率 >= MinExpectedProfitRate
    /// 6. 冷却时间已过
    /// </summary>
    public override Task<StrategySignal?> EvaluateEntryAsync(MarketSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        var options = _optionsMonitor.CurrentValue;
        options.Validate();

        if (!options.Enabled || State != StrategyState.Running)
        {
            return Task.FromResult<StrategySignal?>(null);
        }

        var now = DateTimeOffset.UtcNow;
        var market = snapshot.Market;

        // 检查市场是否在结算时间窗口内
        if (!market.ExpiresAtUtc.HasValue)
        {
            return Task.FromResult<StrategySignal?>(null);
        }

        var timeToExpiry = market.ExpiresAtUtc.Value - now;
        if (timeToExpiry.TotalSeconds > options.MaxSecondsToExpiry ||
            timeToExpiry.TotalSeconds < options.MinSecondsToExpiry)
        {
            return Task.FromResult<StrategySignal?>(null);
        }

        var state = GetMarketState(market.MarketId);

        // 检查冷却时间
        if (state.LastEntryAttemptUtc.HasValue &&
            now - state.LastEntryAttemptUtc.Value < TimeSpan.FromSeconds(options.EntryCooldownSeconds))
        {
            return Task.FromResult<StrategySignal?>(null);
        }

        // 已有持仓，不再入场
        if (state.HasPosition)
        {
            return Task.FromResult<StrategySignal?>(null);
        }

        // 获取订单簿最优卖价
        var yesTop = snapshot.YesTopOfBook;
        var noTop = snapshot.NoTopOfBook;

        if (yesTop?.BestAskPrice is null || noTop?.BestAskPrice is null)
        {
            return Task.FromResult<StrategySignal?>(null);
        }

        // 分析哪一方是高胜率方
        var yesAsk = yesTop.BestAskPrice.Value;
        var noAsk = noTop.BestAskPrice.Value;

        OutcomeSide? targetOutcome = null;
        decimal targetPrice = 0m;
        string targetTokenId = "";

        // 检查 Yes 方是否满足入场条件
        if (yesAsk >= options.MinWinProbability && yesAsk <= options.MaxEntryPrice)
        {
            var expectedProfit = 1m - yesAsk;
            if (expectedProfit >= options.MinExpectedProfitRate)
            {
                targetOutcome = OutcomeSide.Yes;
                targetPrice = yesAsk;
                targetTokenId = market.TokenIds[0];
            }
        }

        // 检查 No 方是否满足入场条件（如果 Yes 不满足或 No 更优）
        if (noAsk >= options.MinWinProbability && noAsk <= options.MaxEntryPrice)
        {
            var expectedProfit = 1m - noAsk;
            if (expectedProfit >= options.MinExpectedProfitRate)
            {
                // 如果两边都满足，选择胜率更高（价格更高）的一方
                if (targetOutcome is null || noAsk > targetPrice)
                {
                    targetOutcome = OutcomeSide.No;
                    targetPrice = noAsk;
                    targetTokenId = market.TokenIds[1];
                }
            }
        }

        // 没有满足条件的入场机会
        if (targetOutcome is null)
        {
            return Task.FromResult<StrategySignal?>(null);
        }

        // 检查深度
        var targetTop = targetOutcome == OutcomeSide.Yes ? yesTop : noTop;
        if (targetTop.BestAskSize is null || targetTop.BestAskSize.Value < options.MinOrderQuantity)
        {
            return Task.FromResult<StrategySignal?>(null);
        }

        // 计算限价（允许一定滑点）
        var limitPrice = Math.Min(0.99m, targetPrice * (1 + options.MaxSlippage));
        var expectedProfit1 = 1m - limitPrice;

        if (expectedProfit1 < options.MinExpectedProfitRate)
        {
            return Task.FromResult<StrategySignal?>(null);
        }

        // 计算下单数量
        var maxQtyByOrderNotional = options.MaxNotionalPerOrder / Math.Max(limitPrice, 0.01m);
        var maxQtyByMarketNotional = options.MaxNotionalPerMarket / Math.Max(limitPrice, 0.01m);
        var availableQty = targetTop.BestAskSize.Value;
        var quantity = Math.Min(options.DefaultOrderQuantity,
            Math.Min(availableQty, Math.Min(maxQtyByOrderNotional, maxQtyByMarketNotional)));

        if (quantity < options.MinOrderQuantity)
        {
            return Task.FromResult<StrategySignal?>(null);
        }

        // 更新状态
        state.LastEntryAttemptUtc = now;

        // 构造买入订单
        var orders = new List<StrategyOrderIntent>
        {
            new(
                market.MarketId,
                targetTokenId,
                targetOutcome.Value,
                OrderSide.Buy,
                OrderType.Limit,
                TimeInForce.Gtc,  // 持有到结算
                limitPrice,
                quantity,
                market.Slug?.Contains("neg", StringComparison.OrdinalIgnoreCase) == true,
                OrderLeg.First)
        };

        var signal = new StrategySignal(
            StrategySignalType.Entry,
            market.MarketId,
            $"High probability {targetOutcome}: Price={targetPrice:F4}, ExpectedProfit={expectedProfit1:P2}, TimeToExpiry={timeToExpiry.TotalMinutes:F1}min",
            orders,
            $"{{\"outcome\":\"{targetOutcome}\",\"price\":{targetPrice:F4},\"expectedProfit\":{expectedProfit1:F4},\"timeToExpirySec\":{timeToExpiry.TotalSeconds:F0}}}");

        _logger.LogInformation(
            "[Endgame] 发现高胜率入场机会: MarketId={MarketId}, Outcome={Outcome}, Price={Price:F4}, TimeToExpiry={TimeToExpiry}",
            market.MarketId, targetOutcome, targetPrice, timeToExpiry);

        return Task.FromResult<StrategySignal?>(signal);
    }

    /// <summary>
    /// 评估出场信号。
    /// 尾盘扫货策略通常持有到结算，不主动出场。
    /// 仅在极端情况（如价格大幅下跌预示可能翻盘）时考虑止损。
    /// </summary>
    public override Task<StrategySignal?> EvaluateExitAsync(MarketSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        // 该策略通常持有到结算，不主动出场
        // 未来可以添加止损逻辑：如果持仓价格大幅下跌（如低于 0.70），可能需要止损
        return Task.FromResult<StrategySignal?>(null);
    }

    /// <summary>
    /// 处理订单状态更新回调。
    /// </summary>
    public override Task OnOrderUpdateAsync(StrategyOrderUpdate update, CancellationToken cancellationToken = default)
    {
        var state = GetMarketState(update.MarketId);

        if (update.SignalType == StrategySignalType.Entry)
        {
            if (update.Status == ExecutionStatus.Filled || update.Status == ExecutionStatus.PartiallyFilled)
            {
                state.HasPosition = true;
                state.EntryOutcome = update.Outcome;
                state.EntryPrice = update.Price;
                state.EntryQuantity = update.FilledQuantity;
                state.EntryFilledUtc = update.TimestampUtc;

                _logger.LogInformation(
                    "[Endgame] 入场成功: MarketId={MarketId}, Outcome={Outcome}, Price={Price:F4}, Qty={Qty}",
                    update.MarketId, update.Outcome, update.Price, update.FilledQuantity);
            }
            else if (update.Status is ExecutionStatus.Cancelled or ExecutionStatus.Rejected or ExecutionStatus.Expired)
            {
                // 订单失败，重置状态
                _logger.LogWarning(
                    "[Endgame] 入场订单失败: MarketId={MarketId}, Status={Status}",
                    update.MarketId, update.Status);
            }
        }

        // 市场结算后，清理状态
        // 注：实际结算处理可能需要单独的事件或定时任务
        return Task.CompletedTask;
    }

    /// <summary>
    /// 获取或创建市场状态。
    /// </summary>
    private MarketState GetMarketState(string marketId)
    {
        return _marketStates.GetOrAdd(marketId, id => new MarketState(id));
    }

    /// <summary>
    /// 单个市场的状态内部类。
    /// </summary>
    private sealed class MarketState
    {
        public MarketState(string marketId)
        {
            MarketId = marketId;
        }

        /// <summary>
        /// 市场 ID。
        /// </summary>
        public string MarketId { get; }

        /// <summary>
        /// 上次入场尝试时间。
        /// </summary>
        public DateTimeOffset? LastEntryAttemptUtc { get; set; }

        /// <summary>
        /// 是否有持仓。
        /// </summary>
        public bool HasPosition { get; set; }

        /// <summary>
        /// 入场方向。
        /// </summary>
        public OutcomeSide? EntryOutcome { get; set; }

        /// <summary>
        /// 入场价格。
        /// </summary>
        public decimal EntryPrice { get; set; }

        /// <summary>
        /// 入场数量。
        /// </summary>
        public decimal EntryQuantity { get; set; }

        /// <summary>
        /// 入场成交时间。
        /// </summary>
        public DateTimeOffset? EntryFilledUtc { get; set; }
    }
}
