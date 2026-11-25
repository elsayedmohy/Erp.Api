namespace ErpDashboard.Api.Features.Sales.Orders;

public interface IOrderQueries
{
    Task<PagedResult<OrderResponse>> ListAsync(OrderQuery query, CancellationToken ct);
    Task<OrderDetailsResponse> GetByIdAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<OrderStatusChangeResponse>> GetHistoryAsync(Guid id, CancellationToken ct);
}

public sealed class OrderQueries(AppDbContext dbContext) : IOrderQueries
{
    private static readonly SortMap<Order> Sorts = new SortMap<Order>()
        .Add("orderDate", o => o.OrderDate)
        .Add("totalAmount", o => o.TotalAmount)
        .Add("status", o => o.Status)
        .Default("orderDate", descending: true)
        .TieBreaker(o => o.Id);

    public Task<PagedResult<OrderResponse>> ListAsync(OrderQuery query, CancellationToken ct)
    {
        if (query.From > query.To)
            throw new RequestValidationException("from", "'from' must be before or equal to 'to'.");

        var q = dbContext.Orders.IgnoreQueryFilters([QueryFilters.SoftDelete]).AsNoTracking();

        if (query.GetSearchPattern() is { } p)
            q = q.Where(o => EF.Functions.ILike(o.Id.ToString(), p, @"\"));

        if (query.Status is { } status) q = q.Where(o => o.Status == status);
        if (query.ShowroomId is { } cid) q = q.Where(o => o.ShowroomId == cid);
        if (query.From is { } from) q = q.Where(o => o.OrderDate >= from);
        if (query.To is { } to) q = q.Where(o => o.OrderDate <= to);

        return Sorts.Apply(q, query.SortBy, query.SortDir)
            .Select(OrderMappings.AsResponse)
            .ToPagedResultAsync(query, ct);
    }

    public async Task<OrderDetailsResponse> GetByIdAsync(Guid id, CancellationToken ct)
    {
        var order = await dbContext.Orders.IgnoreQueryFilters([QueryFilters.SoftDelete]).AsNoTracking()
                        .Include(o => o.Showroom)
                        .Include(o => o.Invoice)
                        .Include(o => o.OrderLines)
                        .FirstOrDefaultAsync(o => o.Id == id, ct)
                    ?? throw new NotFoundException("Order", id);

        return order.ToDetailsResponse();
    }

    public async Task<IReadOnlyList<OrderStatusChangeResponse>> GetHistoryAsync(Guid id, CancellationToken ct)
    {
        if (!await dbContext.Orders.AnyAsync(o => o.Id == id, ct))
            throw new NotFoundException("Order", id);

        return await (
                from c in dbContext.OrderStatusChanges.AsNoTracking()
                where c.OrderId == id
                join u in dbContext.Users on c.ChangedById equals (Guid?)u.Id into users
                from u in users.DefaultIfEmpty()
                orderby c.ChangedAt, c.Id
                select new OrderStatusChangeResponse(
                    c.FromStatus,
                    c.ToStatus,
                    c.ChangedById,
                    u == null ? null : u.FirstName + " " + u.LastName,
                    c.ChangedAt,
                    c.Reason))
            .ToListAsync(ct);
    }
}
