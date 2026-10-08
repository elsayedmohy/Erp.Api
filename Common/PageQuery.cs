
namespace ErpDashboard.Api.Common;

public class PageQuery
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    private int _page = 1;
    private int _pageSize = DefaultPageSize;

    public int Page
    {
        get => _page;
        set => _page = value < 1 ? 1 : value;
    }

    public int PageSize
    {
        get => _pageSize;
        set => _pageSize = value switch
        {
            < 1 => DefaultPageSize,
            > MaxPageSize => MaxPageSize,
            _ => value
        };
    }

    [MaxLength(100)]
    public string? Search { get; set; }

    public string? SortBy { get; set; }
    public string? SortDir { get; set; }

    public string? GetSearchPattern()
    {
        if (string.IsNullOrWhiteSpace(Search)) return null;

        var escaped = Search.Trim()
            .Replace(@"\", @"\\")
            .Replace("%", @"\%")
            .Replace("_", @"\_");

        return $"%{escaped}%";
    }
}