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
        lock (_requests) _requests.Clear();
    }
}