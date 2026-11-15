namespace ErpDashboard.Api.Features.Payments;

public sealed class NotConfiguredPaymentGateway : IPaymentGateway
{
    public string Name => "none";

    public Task<CheckoutSession> CreateCheckoutAsync(CheckoutRequest request, CancellationToken ct) =>
        throw new PaymentProviderUnavailableException("Online payments are not configured.");
}