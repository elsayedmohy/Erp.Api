using ErpDashboard.Api.Data.Enums;
using ErpDashboard.Api.Data.Seed;
using ErpDashboard.Api.Features.Shipping;

namespace ErpDashboard.Api.UnitTests;

public class ShippingQuoteCalculatorTests
{
    private static CarrierPricing Fleet(decimal? price) => new(CarrierIds.CompanyFleet, "Company trucks", true, price);
    private static CarrierPricing Outside(string name, decimal? price) => new(Guid.NewGuid(), name, false, price);

    [Fact]
    public void Cheapest_IsTheLowestAvailablePrice()
    {
        var cheap = Outside("Hag Ali", 1500m);
        var result = ShippingQuoteCalculator.Build(Governorate.Giza,
            [Outside("Hag Mahmoud", 2500m), cheap, Fleet(1800m)]);

        Assert.Equal(cheap.Id, result.CheapestCarrierId);
        Assert.Equal([1500m, 1800m, 2500m], result.Options.Select(o => o.VehiclePrice!.Value));
    }

    [Fact]
    public void SamePrice_PrefersCompanyFleet_ThenName()
    {
        var result = ShippingQuoteCalculator.Build(Governorate.Cairo,
            [Outside("Zaki", 1000m), Outside("Adel", 1000m), Fleet(1000m)]);

        Assert.Equal(["Company trucks", "Adel", "Zaki"], result.Options.Select(o => o.CarrierName));
        Assert.Equal(CarrierIds.CompanyFleet, result.CheapestCarrierId);
    }
    

    [Fact]
    public void CarrierWithoutPrice_IsUnavailable_AndListedAfterAvailableOnes()
    {
        var result = ShippingQuoteCalculator.Build(Governorate.Damietta,
            [Outside("No price", null), Outside("Priced", 3000m)]);

        Assert.Equal("Priced", result.Options[0].CarrierName);
        Assert.False(result.Options[1].Available);
        Assert.Contains("Damietta", result.Options[1].UnavailableReason);
    }

    [Fact]
    public void NothingAvailable_CheapestIsNull()
    {
        var result = ShippingQuoteCalculator.Build(Governorate.Matrouh, [Fleet(null), Outside("Hag Ali", null)]);

        Assert.Null(result.CheapestCarrierId);
        Assert.All(result.Options, o => Assert.False(o.Available));
    }
}