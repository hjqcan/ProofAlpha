using Autotrade.Trading.Application.Contract.Repositories;
using Autotrade.Trading.Domain.Events;
using Autotrade.Trading.Domain.Shared.Enums;
using Microsoft.Extensions.Logging;
using NetDevPack.Messaging;

namespace Autotrade.Trading.Application.EventHandlers;

/// <summary>
/// 成交执行事件处理器。
/// 写入 Trade 记录并更新 Position 持仓。
/// </summary>
public sealed class TradeExecutedEventHandler : IDomainEventHandler<TradeExecutedEvent>
{
    private readonly ITradeRepository _tradeRepository;
    private readonly IPositionRepository _positionRepository;
    private readonly ILogger<TradeExecutedEventHandler> _logger;

    public TradeExecutedEventHandler(
        ITradeRepository tradeRepository,
        IPositionRepository positionRepository,
        ILogger<TradeExecutedEventHandler> logger)
    {
        _tradeRepository = tradeRepository ?? throw new ArgumentNullException(nameof(tradeRepository));
        _positionRepository = positionRepository ?? throw new ArgumentNullException(nameof(positionRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task Handle(TradeExecutedEvent @event)
    {
        _logger.LogDebug(
            "Handling TradeExecutedEvent: OrderId={OrderId}, Side={Side}, Price={Price}, Quantity={Quantity}",
            @event.AggregateId,
            @event.Side,
            @event.Price,
            @event.Quantity);

        try
        {
            if (!string.IsNullOrWhiteSpace(@event.ExchangeTradeId))
            {
                var existing = await _tradeRepository
                    .GetByExchangeTradeIdAsync(@event.ExchangeTradeId)
                    .ConfigureAwait(false);
                if (existing is not null)
                {
                    _logger.LogDebug(
                        "Trade already persisted, skipping duplicate: ExchangeTradeId={ExchangeTradeId}",
                        @event.ExchangeTradeId);
                    return;
                }
            }

            // 1. 写入 Trade 记录
            await _tradeRepository.AddAsync(new TradeDto(
                Id: Guid.NewGuid(),
                OrderId: @event.AggregateId,
                TradingAccountId: @event.TradingAccountId,
                ClientOrderId: @event.ClientOrderId,
                StrategyId: @event.StrategyId,
                MarketId: @event.MarketId,
                TokenId: @event.TokenId,
                Outcome: @event.Outcome,
                Side: @event.Side,
                Price: @event.Price,
                Quantity: @event.Quantity,
                ExchangeTradeId: @event.ExchangeTradeId,
                Fee: @event.Fee,
                CorrelationId: @event.CorrelationId,
                CreatedAtUtc: @event.Timestamp)).ConfigureAwait(false);

            // 2. 更新 Position 持仓
            await UpdatePositionAsync(@event).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to handle TradeExecutedEvent: OrderId={OrderId}", @event.AggregateId);
        }
    }

    private async Task UpdatePositionAsync(TradeExecutedEvent @event)
    {
        try
        {
            // 获取或创建持仓
            var positionDto = await _positionRepository.GetOrCreateAsync(
                @event.TradingAccountId,
                @event.MarketId,
                @event.Outcome).ConfigureAwait(false);

            // 计算新的持仓状态
            decimal newQuantity;
            decimal newAverageCost;
            decimal realizedPnl = positionDto.RealizedPnl;

            if (@event.Side == OrderSide.Buy)
            {
                // 买入：增加持仓，更新均价
                var oldQty = positionDto.Quantity;
                newQuantity = oldQty + @event.Quantity;
                newAverageCost = newQuantity == 0m
                    ? 0m
                    : ((positionDto.AverageCost * oldQty) + (@event.Price * @event.Quantity)) / newQuantity;
            }
            else
            {
                // 卖出：减少持仓，计算已实现盈亏
                if (@event.Quantity > positionDto.Quantity)
                {
                    throw new InvalidOperationException(
                        $"卖出数量 {@event.Quantity} 超过持仓数量 {positionDto.Quantity}，拒绝更新持仓。");
                }

                newQuantity = positionDto.Quantity - @event.Quantity;
                newAverageCost = newQuantity == 0m ? 0m : positionDto.AverageCost;
                realizedPnl += (@event.Price - positionDto.AverageCost) * @event.Quantity;
            }

            // 更新持仓
            var updatedPosition = new PositionDto(
                Id: positionDto.Id,
                TradingAccountId: positionDto.TradingAccountId,
                MarketId: positionDto.MarketId,
                Outcome: positionDto.Outcome,
                Quantity: newQuantity,
                AverageCost: newAverageCost,
                RealizedPnl: realizedPnl,
                UpdatedAtUtc: DateTimeOffset.UtcNow);

            await _positionRepository.UpdateAsync(updatedPosition).ConfigureAwait(false);

            _logger.LogDebug(
                "Position updated: Market={MarketId}, Outcome={Outcome}, Quantity={Quantity}, AvgCost={AvgCost}",
                @event.MarketId,
                @event.Outcome,
                newQuantity,
                newAverageCost);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update position: Market={MarketId}", @event.MarketId);
        }
    }
}
