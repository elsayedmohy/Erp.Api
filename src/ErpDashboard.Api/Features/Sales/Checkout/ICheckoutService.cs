namespace ErpDashboard.Api.Features.Sales.Checkout;

public interface ICheckoutService
{
    Task<StartCheckoutResponse> StartAsync(Guid orderId, StartCheckoutRequest request, uint expectedVersion,
        Guid userId, CancellationToken ct);
    Task<CheckoutResponse> GetLatestAsync(Guid orderId, CancellationToken ct);
}