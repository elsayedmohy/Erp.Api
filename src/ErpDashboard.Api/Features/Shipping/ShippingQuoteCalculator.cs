namespace ErpDashboard.Api.Features.Shipping;

public sealed record CarrierPricing(Guid Id, string Name, bool IsCompanyFleet, decimal? VehiclePrice);

public static class ShippingQuoteCalculator
{
    public static ShippingQuoteResponse Build(Governorate governorate, IEnumerable<CarrierPricing> carriers)
    {
        var options = carriers
            .Select(c => ToOption(governorate, c))
            .OrderByDescending(o => o.Available)
            .ThenBy(o => o.VehiclePrice)
            .ThenByDescending(o => o.IsCompanyFleet)
            .ThenBy(o => o.CarrierName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new ShippingQuoteResponse(governorate, options.FirstOrDefault(o => o.Available)?.CarrierId, options);
    }

    private static ShippingQuoteOption ToOption(Governorate g, CarrierPricing c)
    {
        return c.VehiclePrice is { } price
            ? new(c.Id, c.Name, c.IsCompanyFleet, true, price, null)
            : new(c.Id, c.Name, c.IsCompanyFleet, false, null, $"No price set for {g}.");
    }
}