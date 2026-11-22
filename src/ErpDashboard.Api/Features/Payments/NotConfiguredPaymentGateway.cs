namespace ErpDashboard.Api.Features.Payments;

public sealed class NotConfiguredPaymentGateway : IPaymentGateway
{
    public string Name => "none";

    public Task<CheckoutSession> CreateCheckoutAsync(CheckoutRequest request, CancellationToken ct) =>
        throw new PaymentProviderUnavailableException("Online payments are not configured.");
    
    public Task<ReversalResult> VoidAsync(string providerTransactionId, CancellationToken ct) =>
        throw new PaymentProviderUnavailableException("Online payments are not configured.");

    public Task<ReversalResult> RefundAsync(string providerTransactionId, long amountMinor, CancellationToken ct) =>
        throw new PaymentProviderUnavailableException("Online payments are not configured.");

    public Task<TransactionState> GetTransactionAsync(string providerTransactionId, CancellationToken ct)
    {
        throw new PaymentProviderUnavailableException("Online payments are not configured.");
    }
}