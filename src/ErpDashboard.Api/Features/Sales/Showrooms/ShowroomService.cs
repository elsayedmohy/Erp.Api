namespace ErpDashboard.Api.Features.Sales.Showrooms;

public class ShowroomService(AppDbContext dbContext) : IShowroomService
{
    private static readonly SortMap<Showroom> Sorts = new SortMap<Showroom>()
        .Add("name", c => c.Name)
        .Add("email", c => c.Email)
        .Add("city", c => c.City)
        .Add("country", c => c.Country)
        .Default("name")
        .TieBreaker(c => c.Id);

    public Task<PagedResult<ShowroomResponse>> ListAsync(ShowroomQuery query, CancellationToken ct)
    {
        var q = dbContext.Showrooms.AsNoTracking();

        if (query.GetSearchPattern() is { } p)
            q = q.Where(c =>
                EF.Functions.ILike(c.Name, p, @"\") ||
                EF.Functions.ILike(c.Email, p, @"\") ||
                EF.Functions.ILike(c.PhoneNumber, p, @"\"));

        return Sorts.Apply(q, query.SortBy, query.SortDir)
            .Select(c => ToResponse(c)).ToPagedResultAsync(query, ct);
    }

    public async Task<ShowroomResponse> GetByIdAsync(Guid id, CancellationToken ct)
    {
        Showroom? showroom = await dbContext.Showrooms.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (showroom == null)
            throw new NotFoundException("Showroom", id);

        return ToResponse(showroom);
    }

    public async Task<ShowroomResponse> CreateAsync(ShowroomRequest request, CancellationToken ct)
    {
        await EnsureUniqueAsync(request, exceptId: null, ct);

        var showroom = new Showroom
        {
            Name = request.Name,
            Email = request.Email,
            PhoneNumber = request.PhoneNumber,
            Address = request.Address,
            City = request.City,
            Country = request.Country,
            TaxId = request.TaxId
        };

        dbContext.Showrooms.Add(showroom);
        await dbContext.SaveChangesAsync(ct);
        return ToResponse(showroom);
    }

    public async Task<ShowroomResponse> UpdateAsync(Guid id, ShowroomRequest request, CancellationToken ct)
    {
        var showroom = await dbContext.Showrooms.FirstOrDefaultAsync(c => c.Id == id, ct)
                       ?? throw new NotFoundException("Showroom", id);
        await EnsureUniqueAsync(request, exceptId: id, ct);

        showroom.Name = request.Name;
        showroom.Email = request.Email;
        showroom.PhoneNumber = request.PhoneNumber;
        showroom.Address = request.Address;
        showroom.City = request.City;
        showroom.Country = request.Country;
        showroom.TaxId = request.TaxId;
        await dbContext.SaveChangesAsync(ct);
        return ToResponse(showroom);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        Showroom? showroom = await dbContext.Showrooms.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (showroom == null)
            throw new NotFoundException("Showroom", id);

        var openOrders = await dbContext.Orders.IgnoreQueryFilters([QueryFilters.Showroom])
            .CountAsync(o => o.ShowroomId == id && OpenOrderStatuses.Contains(o.Status), ct);
        var unpaidInvoices = await dbContext.Invoices.IgnoreQueryFilters([QueryFilters.Showroom])
            .Where(i => i.ShowroomId == id && i.Status == InvoiceStatus.Issued)
            .WithBalance()
            .CountAsync(b => b.Paid + b.Credited < b.Invoice.TotalAmount, ct);

        if (openOrders > 0 || unpaidInvoices > 0)
            throw new ConflictException(
                $"The showroom has {openOrders} open order(s) and {unpaidInvoices} unpaid invoice(s). " +
                "Finish or cancel the orders and settle the invoices before deleting it.",
                code: "showroom_has_open_business",
                extensions: new Dictionary<string, object?>
                {
                    ["openOrders"] = openOrders,
                    ["unpaidInvoices"] = unpaidInvoices
                });

        dbContext.Remove(showroom);
        await dbContext.SaveChangesAsync(ct);
    }


    private static readonly OrderStatus[] OpenOrderStatuses =
    [
        OrderStatus.Draft, OrderStatus.PendingApproval, OrderStatus.Confirmed, OrderStatus.Preparing, OrderStatus.Shipped
    ];


    private async Task EnsureUniqueAsync(ShowroomRequest request, Guid? exceptId, CancellationToken ct)
    {
        var others = dbContext.Showrooms.Where(s => s.Id != exceptId);

        if (await others.AnyAsync(s => s.TaxId == request.TaxId, ct))
            throw new ConflictException("A showroom with this tax ID already exists.");

        if (request.Email is not null && await others.AnyAsync(s => s.Email == request.Email, ct))
            throw new ConflictException("A showroom with this email already exists.");
    }
    

    private static ShowroomResponse ToResponse(Showroom showroom) => new ShowroomResponse(
        showroom.Id,
        showroom.Name,
        showroom.Email,
        showroom.PhoneNumber,
        showroom.Address,
        showroom.City,
        showroom.Country,
        showroom.TaxId);
}