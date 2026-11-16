namespace ErpDashboard.Api.Features.Shipping;

public sealed record CreateCarrierRequest(
    [Required, MaxLength(100)] string Name,
    [Required, MaxLength(20)] string Phone,
    [MaxLength(500)] string? Notes = null);

public sealed record UpdateCarrierRequest(
    [Required, MaxLength(100)] string Name,
    [MaxLength(20)] string? Phone,
    bool IsActive,
    [MaxLength(500)] string? Notes = null);

public sealed record SetCarrierRateRequest(
    [Range(typeof(decimal), "0.01", "10000000")] decimal VehiclePrice);

public sealed record CarrierRateResponse(Governorate Governorate, decimal VehiclePrice);

public sealed record CarrierResponse(
    Guid Id, string Name, string? Phone, string? Notes,
    bool IsCompanyFleet, bool IsActive, List<CarrierRateResponse> Rates);

public sealed record ShippingQuoteOption(
    Guid CarrierId, string CarrierName, bool IsCompanyFleet,
    bool Available, decimal? VehiclePrice, string? UnavailableReason);

public sealed record ShippingQuoteResponse(
    Governorate Governorate, Guid? CheapestCarrierId, List<ShippingQuoteOption> Options);