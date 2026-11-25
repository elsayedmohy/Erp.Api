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
        var supplier = await dbContext.Suppliers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct)
                       ?? throw new NotFoundException("Supplier", id);

        return ToResponse(supplier);
    }

    public async Task<SupplierResponse> CreateAsync(SupplierRequest request, CancellationToken ct)
    {
        var email = request.Email.Trim();
        var phone = request.Phone.Trim();
        await EnsureUniqueAsync(email, phone, exceptId: null, ct);

        var supplier = new Supplier
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            ContactName = request.ContactName.Trim(),
            Email = email,
            Phone = phone,
            PaymentTerms = request.PaymentTerms
        };

        dbContext.Suppliers.Add(supplier);
        await dbContext.SaveChangesAsync(ct);
        return ToResponse(supplier);
    }

    public async Task<SupplierResponse> UpdateAsync(Guid id, SupplierRequest request, CancellationToken ct)
    {
        var supplier = await dbContext.Suppliers.FirstOrDefaultAsync(c => c.Id == id, ct)
                       ?? throw new NotFoundException("Supplier", id);

        var email = request.Email.Trim();
        var phone = request.Phone.Trim();
        await EnsureUniqueAsync(email, phone, exceptId: id, ct);

        supplier.Name = request.Name.Trim();
        supplier.ContactName = request.ContactName.Trim();
        supplier.Email = email;
        supplier.Phone = phone;
        supplier.PaymentTerms = request.PaymentTerms;
        await dbContext.SaveChangesAsync(ct);
        return ToResponse(supplier);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var supplier = await dbContext.Suppliers.FirstOrDefaultAsync(c => c.Id == id, ct)
                       ?? throw new NotFoundException("Supplier", id);

        if (await dbContext.PurchaseOrders.AnyAsync(o => o.SupplierId == id, ct))
            throw new ConflictException("The supplier has purchase orders and cannot be deleted.", code: "in_use");

        dbContext.Remove(supplier);
        await dbContext.SaveChangesAsync(ct);
    }

    private async Task EnsureUniqueAsync(string email, string phone, Guid? exceptId, CancellationToken ct)
    {
        var lowered = email.ToLower();
        if (await dbContext.Suppliers.AnyAsync(
                s => s.Id != exceptId && (s.Email.ToLower() == lowered || s.Phone == phone), ct))
            throw new ConflictException("A supplier with this email or phone already exists.");
    }

    private static SupplierResponse ToResponse(Supplier supplier) => new(
        supplier.Id,
        supplier.Name,
        supplier.ContactName,
        supplier.Email,
        supplier.Phone,
        supplier.PaymentTerms);
}