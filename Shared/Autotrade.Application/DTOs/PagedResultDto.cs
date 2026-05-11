namespace Autotrade.Application.DTOs;

/// <summary>
/// Shared paged query result DTO.
/// </summary>
/// <typeparam name="T">The item type.</typeparam>
public class PagedResultDto<T>
{
    public IReadOnlyList<T> Items { get; set; } = Array.Empty<T>();

    public long TotalCount { get; set; }

    public int PageIndex { get; set; } = 1;

    public int PageSize { get; set; } = 10;

    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling(TotalCount / (double)PageSize) : 0;

    public bool HasPreviousPage => PageIndex > 1;

    public bool HasNextPage => PageIndex < TotalPages;

    public PagedResultDto()
    {
    }

    public PagedResultDto(IReadOnlyList<T> items, long totalCount, int pageIndex, int pageSize)
    {
        ArgumentNullException.ThrowIfNull(items);

        Items = items;
        TotalCount = Math.Max(0, totalCount);
        PageIndex = Math.Max(1, pageIndex);
        PageSize = Math.Max(1, pageSize);
    }
}

/// <summary>
/// Shared paged query request DTO.
/// </summary>
public class PagedRequestDto
{
    private const int DefaultPageSize = 10;
    private const int MaxPageSize = 1_000;

    private int _pageIndex = 1;
    private int _pageSize = DefaultPageSize;

    public int PageIndex
    {
        get => _pageIndex;
        set => _pageIndex = Math.Max(1, value);
    }

    public int PageSize
    {
        get => _pageSize;
        set => _pageSize = Math.Clamp(value, 1, MaxPageSize);
    }

    public string? SortBy { get; set; }

    public bool Descending { get; set; }

    public int Skip => (PageIndex - 1) * PageSize;

    public void Normalize()
    {
        PageIndex = _pageIndex;
        PageSize = _pageSize;
    }
}
