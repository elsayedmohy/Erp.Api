namespace ErpDashboard.Api.Features.Purchasing.Suppliers;

public class SupplierService(AppDbContext dbContext) : ISupplierService
{
    private static readonly SortMap<Supplier> Sorts = new SortMap<Supplier>()
        .Add("name", c => c.Name)
        .Add("contactName", c => c.ContactName)
        .Add("email", c => c.Email)
        .Add("phone", c => c.Phone)
        .Add("paymentTerms", c => c.PaymentTerms)
        .Default("name")
        .TieBreaker(c => c.Id);

    public Task<PagedResult<SupplierResponse>> ListAsync(SupplierQuery query, CancellationToken ct)
    {
        var q = dbContext.Suppliers.AsNoTracking();

        if (query.GetSearchPattern() is { } p)
            q = q.Where(s =>
                EF.Functions.ILike(s.Name, p, @"\") ||
                EF.Functions.ILike(s.Email, p, @"\") ||
                EF.Functions.ILike(s.Phone, p, @"\"));

        return Sorts.Apply(q, query.SortBy, query.SortDir)
            .Select(s => ToResponse(s)).ToPagedResultAsync(query, ct);
    }

    public async Task<SupplierResponse> GetByIdAsync(Guid id, CancellationToken ct)
    {
        Supplier? supplier = await dbContext.Suppliers.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (supplier == null)
            throw new NotFoundException("Supplier", id);

        return ToResponse(supplier);
    }

    public Task<SupplierResponse> CreateAsync(SupplierRequest request, CancellationToken ct)
    {
        var existingSupplier = dbContext.Suppliers
            .AnyAsync(c => c.Email == request.Email ||
                           c.Phone == request.Phone, cancellationToken: ct);

        if (existingSupplier != null)
            throw new BadRequestException("Supplier already exists");

        Supplier supplier = new Supplier
        {
            Name = request.Name,
            ContactName = request.ContactName,
            Email = request.Email,
            Phone = request.Phone,
            PaymentTerms = request.PaymentTerms,
        };

        dbContext.Suppliers.Add(supplier);
        return Task.FromResult(ToResponse(supplier));
    }

    public async Task<SupplierResponse> UpdateAsync(Guid id, SupplierRequest request, CancellationToken ct)
    {
        Supplier? supplier = await dbContext.Suppliers.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (supplier == null)
            throw new NotFoundException("Supplier", id);

        supplier.ContactName = request.ContactName;
        supplier.Email = request.Email;
        supplier.Phone = request.Phone;
        supplier.PaymentTerms = request.PaymentTerms;
        await dbContext.SaveChangesAsync(ct);
        return ToResponse(supplier);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        Supplier? supplier = await dbContext.Suppliers.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (supplier == null)
            throw new NotFoundException("Supplier", id);

        dbContext.Remove(supplier);
        await dbContext.SaveChangesAsync(ct);
    }


    private static SupplierResponse ToResponse(Supplier supplier) => new SupplierResponse(
        supplier.Id,
        supplier.Name,
        supplier.ContactName,
        supplier.Email,
        supplier.Phone,
        supplier.PaymentTerms
        );
}