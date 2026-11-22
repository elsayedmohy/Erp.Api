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
        InquiryUnavailable = false;
        lock (_state) _state.Clear();
        lock (_inquiries) _inquiries.Clear();
        lock (_requests) _requests.Clear();
        lock (_reversals) _reversals.Clear();
    }
    
    public enum ReversalBehavior { Accept, Reject, Unavailable, AcceptThenUnavailable }

    public bool InquiryUnavailable { get; set; }
    private readonly Dictionary<string, TransactionState> _state = [];
    private readonly List<string> _inquiries = [];
    public IReadOnlyList<string> Inquiries { get { lock (_inquiries) return [.. _inquiries]; } }

    public ReversalBehavior VoidBehavior { get; set; } = ReversalBehavior.Accept;
    public ReversalBehavior RefundBehavior { get; set; } = ReversalBehavior.Accept;
    private readonly List<(string Kind, string TransactionId, long? Amount)> _reversals = [];
    public IReadOnlyList<(string Kind, string TransactionId, long? Amount)> Reversals
    { get { lock (_reversals) return [.. _reversals]; } }

    public Task<ReversalResult> VoidAsync(string transactionId, CancellationToken ct) =>
        Reverse("void", transactionId, null, VoidBehavior);

    public Task<ReversalResult> RefundAsync(string transactionId, long amountMinor, CancellationToken ct) =>
        Reverse("refund", transactionId, amountMinor, RefundBehavior);

    public Task<TransactionState> GetTransactionAsync(string transactionId, CancellationToken ct)
    {
        lock (_inquiries) _inquiries.Add(transactionId);
        if (InquiryUnavailable)
            throw new PaymentProviderUnavailableException("Fake inquiry unavailable.");
        lock (_state)
            return Task.FromResult(_state.GetValueOrDefault(transactionId, new TransactionState(false, 0)));
    }

    private Task<ReversalResult> Reverse(string kind, string id, long? amount, ReversalBehavior behavior)
    {
        lock (_reversals) _reversals.Add((kind, id, amount));

        if (behavior is ReversalBehavior.Accept or ReversalBehavior.AcceptThenUnavailable)
            lock (_state)
                _state[id] = kind == "void" ? new TransactionState(true, 0) : new TransactionState(false, amount ?? 0);

        return behavior switch
        {
            ReversalBehavior.Accept => Task.FromResult(ReversalResult.Ok($"{kind}-{id}")),
            ReversalBehavior.Reject => Task.FromResult(ReversalResult.Rejected($"Fake {kind} rejected.")),
            // AcceptThenUnavailable: the provider did it, the answer was lost.
            _ => throw new PaymentProviderUnavailableException($"Fake {kind} unavailable.")
        };
    }
}