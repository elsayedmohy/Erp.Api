namespace ErpDashboard.Api.Extensions;


public static class QueryableExtensions
{
    public static async Task<PagedResult<T>> ToPagedResultAsync<T>(
        this IQueryable<T> query, PageQuery page, CancellationToken ct = default)
    {
        var total = await query.CountAsync(ct);

        var skip = (long)(page.Page - 1) * page.PageSize;
        if (skip >= total)
            return new PagedResult<T>([], page.Page, page.PageSize, total);

        var items = await query.Skip((int)skip).Take(page.PageSize).ToListAsync(ct);
        return new PagedResult<T>(items, page.Page, page.PageSize, total);
    }
}