using Autotrade.MarketData.Domain.Shared.Enums;

namespace Autotrade.MarketData.Application.Contract.Repositories;

/// <summary>
/// 市场仓储接口。
/// </summary>
public interface IMarketRepository
{
    /// <summary>
    /// 根据 ID 获取市场。
    /// </summary>
    Task<MarketDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// 根据 MarketId（业务主键）获取市场。
    /// </summary>
    Task<MarketDto?> GetByMarketIdAsync(string marketId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取所有市场。
    /// </summary>
    Task<IReadOnlyList<MarketDto>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 根据状态获取市场。
    /// </summary>
    Task<IReadOnlyList<MarketDto>> GetByStatusAsync(MarketStatus status, CancellationToken cancellationToken = default);

    /// <summary>
    /// 批量添加或更新市场（Upsert）。
    /// </summary>
    Task UpsertRangeAsync(IEnumerable<MarketDto> markets, CancellationToken cancellationToken = default);

    /// <summary>
    /// 添加市场。
    /// </summary>
    Task AddAsync(MarketDto market, CancellationToken cancellationToken = default);

    /// <summary>
    /// 更新市场。
    /// </summary>
    Task UpdateAsync(MarketDto market, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取市场总数。
    /// </summary>
    Task<int> CountAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// 市场 DTO。
/// </summary>
public sealed record MarketDto(
    Guid Id,
    string MarketId,
    string Name,
    MarketStatus Status,
    DateTimeOffset? ExpiresAtUtc,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);
