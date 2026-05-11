using Autotrade.MarketData.Domain.Shared.Enums;
using NetDevPack.Domain;

namespace Autotrade.MarketData.Domain.Entities;

/// <summary>
/// 市场聚合根：表达一个 Polymarket 市场的元数据。
/// </summary>
public sealed class Market : Entity, IAggregateRoot
{
    // EF Core
    private Market()
    {
        MarketId = string.Empty;
        Name = string.Empty;
        Status = MarketStatus.Unknown;
        CreatedAtUtc = DateTimeOffset.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
    }

    public Market(string marketId, string name, DateTimeOffset? expiresAtUtc = null)
    {
        if (string.IsNullOrWhiteSpace(marketId))
        {
            throw new ArgumentException("市场 ID 不能为空", nameof(marketId));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("市场名称不能为空", nameof(name));
        }

        MarketId = marketId.Trim();
        Name = name.Trim();
        ExpiresAtUtc = expiresAtUtc;
        Status = MarketStatus.Active;
        CreatedAtUtc = DateTimeOffset.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
    }

    /// <summary>
    /// 外部市场 ID（业务主键/自然键）。
    /// </summary>
    public string MarketId { get; private set; }

    public string Name { get; private set; }

    public MarketStatus Status { get; private set; }

    public DateTimeOffset? ExpiresAtUtc { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public void UpdateExpiresAtUtc(DateTimeOffset? expiresAtUtc)
    {
        if (ExpiresAtUtc == expiresAtUtc)
        {
            return;
        }

        ExpiresAtUtc = expiresAtUtc;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("市场名称不能为空", nameof(name));
        }

        Name = name.Trim();
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    public void UpdateStatus(MarketStatus status)
    {
        Status = status;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }
}

