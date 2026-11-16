namespace ErpDashboard.Api.Features.Shipping;

public sealed class CarrierService(AppDbContext dbContext) : ICarrierService
{
    public async Task<List<CarrierResponse>> ListAsync(bool includeInactive, CancellationToken ct)
    {
        var carriers = await dbContext.Carriers.AsNoTracking()
            .Include(c => c.Rates)
            .Where(c => includeInactive || c.IsActive)
            .OrderByDescending(c => c.IsCompanyFleet).ThenBy(c => c.Name)
            .ToListAsync(ct);
        return carriers.Select(ToResponse).ToList();
    }

    public async Task<CarrierResponse> GetByIdAsync(Guid id, CancellationToken ct) =>
        ToResponse(await LoadAsync(id, ct, tracking: false));

    public async Task<CarrierResponse> CreateAsync(CreateCarrierRequest request, CancellationToken ct)
    {
        var carrier = new Carrier
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            Phone = request.Phone.Trim(),
            Notes = Clean(request.Notes),
            IsActive = true
        };
        dbContext.Carriers.Add(carrier);
        await dbContext.SaveChangesAsync(ct);
        return ToResponse(carrier);
    }

    public async Task<CarrierResponse> UpdateAsync(Guid id, UpdateCarrierRequest request, CancellationToken ct)
    {
        var carrier = await LoadAsync(id, ct, tracking: true);

        if (!carrier.IsCompanyFleet && string.IsNullOrWhiteSpace(request.Phone))
            throw new RequestValidationException("phone", "Outside carriers need a phone number.");

        carrier.Name = request.Name.Trim();
        carrier.Phone = Clean(request.Phone);
        carrier.Notes = Clean(request.Notes);
        carrier.IsActive = request.IsActive;
        await dbContext.SaveChangesAsync(ct);
        return ToResponse(carrier);
    }

    public async Task<CarrierResponse> SetRateAsync(
        Guid id, Governorate governorate, SetCarrierRateRequest request, CancellationToken ct)
    {
        EnsureDefined(governorate);
        if (request.VehiclePrice != Math.Round(request.VehiclePrice, 2))
            throw new RequestValidationException("vehiclePrice", "Price cannot have more than 2 decimal places.");

        var carrier = await LoadAsync(id, ct, tracking: true);

        var rate = carrier.Rates.SingleOrDefault(r => r.Governorate == governorate);
        if (rate is null)
            dbContext.CarrierRates.Add(new CarrierRate
            {
                Id = Guid.NewGuid(), CarrierId = carrier.Id,
                Governorate = governorate, VehiclePrice = request.VehiclePrice
            });
        else
            rate.VehiclePrice = request.VehiclePrice;

        await dbContext.SaveChangesAsync(ct);
        return await GetByIdAsync(id, ct);
    }

    public async Task RemoveRateAsync(Guid id, Governorate governorate, CancellationToken ct)
    {
        EnsureDefined(governorate);
        _ = await LoadAsync(id, ct, tracking: false);
        await dbContext.CarrierRates
            .Where(r => r.CarrierId == id && r.Governorate == governorate)
            .ExecuteDeleteAsync(ct);
    }

    public async Task<ShippingQuoteResponse> QuoteAsync(Governorate governorate, CancellationToken ct)
    {
        EnsureDefined(governorate);
        var carriers = await dbContext.Carriers.AsNoTracking()
            .Where(c => c.IsActive)
            .Select(c => new CarrierPricing(
                c.Id, c.Name, c.IsCompanyFleet,
                c.Rates.Where(r => r.Governorate == governorate)
                       .Select(r => (decimal?)r.VehiclePrice).FirstOrDefault()))
            .ToListAsync(ct);

        return ShippingQuoteCalculator.Build(governorate, carriers);
    }

    private async Task<Carrier> LoadAsync(Guid id, CancellationToken ct, bool tracking)
    {
        var q = dbContext.Carriers.Include(c => c.Rates).AsQueryable();
        if (!tracking) q = q.AsNoTracking();
        return await q.SingleOrDefaultAsync(c => c.Id == id, ct)
               ?? throw new NotFoundException(nameof(Carrier), id);
    }

    
    private static void EnsureDefined(Governorate g)
    {
        if (!Enum.IsDefined(g))
            throw new RequestValidationException("governorate", "Unknown governorate.");
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static CarrierResponse ToResponse(Carrier c) => new(
        c.Id, c.Name, c.Phone, c.Notes, c.IsCompanyFleet, c.IsActive,
        c.Rates.OrderBy(r => r.Governorate.ToString())
               .Select(r => new CarrierRateResponse(r.Governorate, r.VehiclePrice)).ToList());
}