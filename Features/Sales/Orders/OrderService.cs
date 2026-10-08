namespace ErpDashboard.Api.Features.Sales.Orders;

public class OrderService(AppDbContext dbContext) :IOrderService
{
    private static readonly SortMap<Order> Sorts = new SortMap<Order>()
        .Add("orderDate", o => o.OrderDate)
        .Add("totalAmount", o => o.TotalAmount)
        .Add("status", o => o.Status)
        .Default("orderDate", descending: true)
        .TieBreaker(o => o.Id);

    public Task<PagedResult<OrderLineResponse>> ListAsync(OrderQuery query, CancellationToken ct)
    {
        if (query.From > query.To)
            throw new BadRequestException("'from' must be before or equal to 'to'.");

        var q = dbContext.Orders.AsNoTracking();

        if (query.Status is { } status)  q = q.Where(o => o.Status == status);
        if (query.CustomerId is { } cid) q = q.Where(o => o.CustomerId == cid);
        if (query.From is { } from)      q = q.Where(o => o.OrderDate >= from);
        if (query.To is { } to)          q = q.Where(o => o.OrderDate <= to);

        return Sorts.Apply(q, query.SortBy, query.SortDir)
            .Select(o => new OrderLineResponse(o.Id, o.CustomerId, o.OrderDate, o.Status, o.TotalAmount))
            .ToPagedResultAsync(query, ct);
    }
}