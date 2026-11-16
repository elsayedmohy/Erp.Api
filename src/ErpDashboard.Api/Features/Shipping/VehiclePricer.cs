namespace ErpDashboard.Api.Features.Shipping;

public sealed record PricedVehicle(Guid CarrierId, string CarrierName, decimal Price);

public interface IVehiclePricer
{
    Task<List<PricedVehicle>> PriceAsync(Governorate governorate, IReadOnlyList<Guid> carrierIds, CancellationToken ct);
}

public sealed class VehiclePricer(AppDbContext dbContext) : IVehiclePricer
{
    public async Task<List<PricedVehicle>> PriceAsync(
        Governorate governorate, IReadOnlyList<Guid> carrierIds, CancellationToken ct)
    {
        var distinct = carrierIds.Distinct().ToList();
        var carriers = await dbContext.Carriers.AsNoTracking()
            .Where(c => distinct.Contains(c.Id))
            .Select(c => new
            {
                c.Id, c.Name, c.IsActive,
                Price = c.Rates.Where(r => r.Governorate == governorate)
                    .Select(r => (decimal?)r.VehiclePrice).FirstOrDefault()
            })
            .ToDictionaryAsync(c => c.Id, ct);

        var errors = new Dictionary<string, string[]>();
        for (var i = 0; i < carrierIds.Count; i++)
        {
            if (!carriers.TryGetValue(carrierIds[i], out var c))
                errors[$"vehicles[{i}].carrierId"] = ["Carrier not found."];
            else if (!c.IsActive)
                errors[$"vehicles[{i}].carrierId"] = [$"{c.Name} is not active."];
            else if (c.Price is null)
                errors[$"vehicles[{i}].carrierId"] = [$"{c.Name} has no price for {governorate}."];
        }
        if (errors.Count > 0)
            throw new RequestValidationException(errors);

        return carrierIds.Select(id => carriers[id])
            .Select(c => new PricedVehicle(c.Id, c.Name, c.Price!.Value))
            .ToList();
    }
}