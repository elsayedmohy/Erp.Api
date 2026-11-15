namespace ErpDashboard.Api.Features.Payments;

public sealed record CheckoutRequest(
    Guid AttemptId,
    long AmountMinor,
    string Currency,
    string Description,
    PayerDetails Payer);

public sealed record PayerDetails(string Name, string Email, string Phone);

public sealed record CheckoutSession(string ProviderReference, Uri CheckoutUrl);

public sealed record StartOnlinePaymentResponse(Guid AttemptId, Uri CheckoutUrl, decimal Amount, string Currency);
