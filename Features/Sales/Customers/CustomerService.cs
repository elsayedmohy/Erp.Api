namespace ErpDashboard.Api.Features.Sales.Customers;

public class CustomerService(AppDbContext dbContext) : ICustomerService
{
    private static readonly SortMap<Customer> Sorts = new SortMap<Customer>()
        .Add("name", c => c.Name)
        .Add("email", c => c.Email)
        .Add("city", c => c.City)
        .Add("country", c => c.Country)
        .Default("name")
        .TieBreaker(c => c.Id);

    public Task<PagedResult<CustomerResponse>> ListAsync(CustomerQuery query, CancellationToken ct)
    {
        var q = dbContext.Customers.AsNoTracking();

        if (query.GetSearchPattern() is { } p)
            q = q.Where(c =>
                EF.Functions.ILike(c.Name, p, @"\") ||
                EF.Functions.ILike(c.Email, p, @"\") ||
                EF.Functions.ILike(c.PhoneNumber, p, @"\"));

        return Sorts.Apply(q, query.SortBy, query.SortDir)
            .Select(c => new CustomerResponse(
                c.Id,
                c.Name,
                c.Email,
                c.PhoneNumber,
                c.City,
                c.Country)).ToPagedResultAsync(query , ct);

    }
}