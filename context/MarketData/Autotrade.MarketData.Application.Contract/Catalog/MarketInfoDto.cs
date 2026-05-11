namespace Autotrade.MarketData.Application.Contract.Catalog;

/// <summary>
/// Market metadata for cross-context usage.
/// </summary>
public sealed record MarketInfoDto
{
    public required string MarketId { get; init; }
    public required string ConditionId { get; init; }
    public required string Name { get; init; }
    public string? Category { get; init; }
    public string? Slug { get; init; }
    public string Status { get; init; } = "";
    public DateTimeOffset? ExpiresAtUtc { get; init; }
    public decimal Volume24h { get; init; }
    public decimal Liquidity { get; init; }
    public IReadOnlyList<string> TokenIds { get; init; } = Array.Empty<string>();
}
