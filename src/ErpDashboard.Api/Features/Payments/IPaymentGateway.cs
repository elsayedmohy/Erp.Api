namespace ErpDashboard.Api.Features.Payments;

public interface IPaymentGateway
{
    string Name { get; }
    Task<CheckoutSession> CreateCheckoutAsync(CheckoutRequest request, CancellationToken ct);
    /// Cancels a same-day payment before settlement. Rejected when it is too late.
    Task<ReversalResult> VoidAsync(string providerTransactionId, CancellationToken ct);

    /// Returns money after settlement.
    Task<ReversalResult> RefundAsync(string providerTransactionId, long amountMinor, CancellationToken ct);
    
    Task<TransactionState> GetTransactionAsync(string providerTransactionId, CancellationToken ct);
}

public sealed record ReversalResult(bool Succeeded, string? ProviderReference, string? Message)
{
    public static ReversalResult Ok(string? reference) => new(true, reference, null);
    public static ReversalResult Rejected(string message) => new(false, null, message);
}
public sealed record TransactionState(bool IsVoided, long RefundedAmountMinor);