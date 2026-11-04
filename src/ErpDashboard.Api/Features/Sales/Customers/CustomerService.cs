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
            .Select(c => ToResponse(c)).ToPagedResultAsync(query, ct);
    }

    public async Task<CustomerResponse> GetByIdAsync(Guid id, CancellationToken ct)
    {
        Customer? customer = await dbContext.Customers.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (customer == null)
            throw new NotFoundException("Customer", id);

        return ToResponse(customer);
    }

    public Task<CustomerResponse> CreateAsync(CustomerRequest request, CancellationToken ct)
    {
        var existingCustomer = dbContext.Customers
            .AnyAsync(c => c.Email == request.Email ||
                           c.TaxId == request.TaxId, cancellationToken: ct);

        if (existingCustomer != null)
            throw new BadRequestException("Customer already exists");

        Customer customer = new Customer
        {
            Name = request.Name,
            Email = request.Email,
            PhoneNumber = request.PhoneNumber,
            Address = request.Address,
            City = request.City,
            Country = request.Country,
            TaxId = request.TaxId
        };

        dbContext.Customers.Add(customer);
        return Task.FromResult(ToResponse(customer));
    }

    public async Task<CustomerResponse> UpdateAsync(Guid id, CustomerRequest request, CancellationToken ct)
    {
        Customer? customer = await dbContext.Customers.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (customer == null)
            throw new NotFoundException("Customer", id);

        customer.Name = request.Name;
        customer.Email = request.Email;
        customer.PhoneNumber = request.PhoneNumber;
        customer.Address = request.Address;
        customer.City = request.City;
        customer.Country = request.Country;
        customer.TaxId = request.TaxId;
        await dbContext.SaveChangesAsync(ct);
        return ToResponse(customer);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        Customer? customer = await dbContext.Customers.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (customer == null)
            throw new NotFoundException("Customer", id);

        dbContext.Remove(customer);
        await dbContext.SaveChangesAsync(ct);
    }


    private static CustomerResponse ToResponse(Customer customer) => new CustomerResponse(
        customer.Id,
        customer.Name,
        customer.Email,
        customer.PhoneNumber,
        customer.Address,
        customer.City,
        customer.Country,
        customer.TaxId);
}