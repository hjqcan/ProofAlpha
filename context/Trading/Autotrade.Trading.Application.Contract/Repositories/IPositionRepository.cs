using Autotrade.Trading.Domain.Shared.Enums;

namespace Autotrade.Trading.Application.Contract.Repositories;

/// <summary>
/// 持仓仓储接口。
/// </summary>
public interface IPositionRepository
{
    /// <summary>
    /// 根据 ID 获取持仓。
    /// </summary>
    Task<PositionDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// 根据交易聚合、市场和结果侧获取持仓。
    /// </summary>
    Task<PositionDto?> GetByMarketAndOutcomeAsync(
        Guid tradingAccountId,
        string marketId,
        OutcomeSide outcome,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取指定交易账户的所有持仓。
    /// </summary>
    Task<IReadOnlyList<PositionDto>> GetByTradingAccountIdAsync(
        Guid tradingAccountId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取所有持仓。
    /// </summary>
    Task<IReadOnlyList<PositionDto>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取非零持仓。
    /// </summary>
    Task<IReadOnlyList<PositionDto>> GetNonZeroAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 根据市场和结果侧获取或创建持仓。
    /// </summary>
    Task<PositionDto> GetOrCreateAsync(
        Guid tradingAccountId,
        string marketId,
        OutcomeSide outcome,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 添加持仓。
    /// </summary>
    Task AddAsync(PositionDto position, CancellationToken cancellationToken = default);

    /// <summary>
    /// 更新持仓。
    /// </summary>
    Task UpdateAsync(PositionDto position, CancellationToken cancellationToken = default);
}

/// <summary>
/// 持仓 DTO。
/// </summary>
public sealed record PositionDto(
    Guid Id,
    Guid TradingAccountId,
    string MarketId,
    OutcomeSide Outcome,
    decimal Quantity,
    decimal AverageCost,
    decimal RealizedPnl,
    DateTimeOffset UpdatedAtUtc)
{
    public decimal UnrealizedPnl(decimal currentPrice) => (currentPrice - AverageCost) * Quantity;
    public decimal Notional => Quantity * AverageCost;
}
