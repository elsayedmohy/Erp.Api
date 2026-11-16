namespace ErpDashboard.Api.Features.Shipping;

public interface ICarrierService
{
    Task<List<CarrierResponse>> ListAsync(bool includeInactive, CancellationToken ct);
    Task<CarrierResponse> GetByIdAsync(Guid id, CancellationToken ct);
    Task<CarrierResponse> CreateAsync(CreateCarrierRequest request, CancellationToken ct);
    Task<CarrierResponse> UpdateAsync(Guid id, UpdateCarrierRequest request, CancellationToken ct);
    Task<CarrierResponse> SetRateAsync(Guid id, Governorate governorate, SetCarrierRateRequest request, CancellationToken ct);
    Task RemoveRateAsync(Guid id, Governorate governorate, CancellationToken ct);
    Task<ShippingQuoteResponse> QuoteAsync(Governorate governorate, CancellationToken ct);
}