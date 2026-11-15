namespace ErpDashboard.Api.Features.Payments;

public interface IPaymentGateway
{
    string Name { get; }
    Task<CheckoutSession> CreateCheckoutAsync(CheckoutRequest request, CancellationToken ct);
}