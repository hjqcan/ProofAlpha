namespace Autotrade.MarketData.Application.Catalog;

/// <summary>
/// 市场目录同步配置（从 Gamma API 定时刷新）。
/// </summary>
public sealed class MarketCatalogSyncOptions
{
    public const string SectionName = "MarketData:CatalogSync";

    /// <summary>
    /// Gamma /markets 每页大小（limit）。
    /// </summary>
    public int PageSize { get; set; } = 100;

    /// <summary>
    /// 最大分页页数（防止无限循环/拉取过量）。
    /// </summary>
    public int MaxPages { get; set; } = 200;

    /// <summary>
    /// 是否拉取 closed=true 的市场（默认只拉取活跃市场）。
    /// </summary>
    public bool IncludeClosed { get; set; } = false;

    public void Validate()
    {
        if (PageSize <= 0 || PageSize > 500)
        {
            throw new ArgumentOutOfRangeException(nameof(PageSize), PageSize, "PageSize must be between 1 and 500.");
        }

        if (MaxPages <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxPages), MaxPages, "MaxPages must be positive.");
        }
    }
}

