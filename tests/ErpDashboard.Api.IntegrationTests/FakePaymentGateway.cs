using ErpDashboard.Api.Common;
using ErpDashboard.Api.Features.Payments;

namespace ErpDashboard.Api.IntegrationTests;

public sealed class FakePaymentGateway : IPaymentGateway
{
    private readonly List<CheckoutRequest> _requests = [];

    public string Name => "paymob";
    public bool Unavailable { get; set; }
    public IReadOnlyList<CheckoutRequest> Requests { get { lock (_requests) return [.. _requests]; } }

    public Task<CheckoutSession> CreateCheckoutAsync(CheckoutRequest request, CancellationToken ct)
    {
        lock (_requests) _requests.Add(request);

        if (Unavailable)
            throw new PaymentProviderUnavailableException("Fake provider is down.");

        return Task.FromResult(new CheckoutSession(
            $"fake-{request.AttemptId:N}",
            new Uri($"https://pay.example.test/checkout/{request.AttemptId:N}")));
    }

    public void Reset()
    {
        Unavailable = false;
        VoidBehavior = ReversalBehavior.Accept;
        RefundBehavior = ReversalBehavior.Accept;
        lock (_requests) _requests.Clear();
        lock (_reversals) _reversals.Clear();
    }
    
    public enum ReversalBehavior { Accept, Reject, Unavailable }

    public ReversalBehavior VoidBehavior { get; set; } = ReversalBehavior.Accept;
    public ReversalBehavior RefundBehavior { get; set; } = ReversalBehavior.Accept;
    private readonly List<(string Kind, string TransactionId, long? Amount)> _reversals = [];
    public IReadOnlyList<(string Kind, string TransactionId, long? Amount)> Reversals
    { get { lock (_reversals) return [.. _reversals]; } }

    public Task<ReversalResult> VoidAsync(string transactionId, CancellationToken ct) =>
        Reverse("void", transactionId, null, VoidBehavior);

    public Task<ReversalResult> RefundAsync(string transactionId, long amountMinor, CancellationToken ct) =>
        Reverse("refund", transactionId, amountMinor, RefundBehavior);

    private Task<ReversalResult> Reverse(string kind, string id, long? amount, ReversalBehavior behavior)
    {
        lock (_reversals) _reversals.Add((kind, id, amount));
        return behavior switch
        {
            ReversalBehavior.Accept => Task.FromResult(ReversalResult.Ok($"{kind}-{id}")),
            ReversalBehavior.Reject => Task.FromResult(ReversalResult.Rejected($"Fake {kind} rejected.")),
            _ => throw new PaymentProviderUnavailableException($"Fake {kind} unavailable.")
        };
    }
}