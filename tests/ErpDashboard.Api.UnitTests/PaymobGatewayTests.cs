using System.Net;
using System.Text;
using System.Text.Json;
using ErpDashboard.Api.Common;
using ErpDashboard.Api.Features.Payments;
using ErpDashboard.Api.Infrastructure.Payments.Paymob;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ErpDashboard.Api.UnitTests;

public class PaymobGatewayTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
        : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastRequest = request;
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return await respond(request, ct);
        }
    }

    private static readonly CheckoutRequest Request = new(
        Guid.Parse("11111111-2222-3333-4444-555555555555"), 102601, "EGP", "Invoice INV-2026-000001",
        new PayerDetails("Cairo Showroom", "cairo@example.test", "01000000000"));

    private static PaymobGateway Gateway(StubHandler handler, int timeoutSeconds = 10) =>
        new(new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan },
            Options.Create(new PaymobOptions
            {
                ApiKey = "api_test",
                SecretKey = "sk_test",
                PublicKey = "pk test+1",
                HmacSecret = "hmac",
                IntegrationId = 4242,
                ApiBaseUrl = "https://api.example.test",
                CheckoutBaseUrl = "https://checkout.example.test",
                TimeoutSeconds = timeoutSeconds
            }),
            NullLogger<PaymobGateway>.Instance);

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task SendsAmountInPiasters_AndAttemptAsReference_WithSecretKey()
    {
        var handler = new StubHandler((_, _) => Task.FromResult(Json(HttpStatusCode.Created,
            """{"id":"pi_1","client_secret":"cs_abc"}""")));

        await Gateway(handler).CreateCheckoutAsync(Request, CancellationToken.None);

        Assert.Equal("https://api.example.test/v1/intention/", handler.LastRequest!.RequestUri!.ToString());
        Assert.Equal("Token sk_test", handler.LastRequest.Headers.GetValues("Authorization").Single());

        using var body = JsonDocument.Parse(handler.LastBody!);
        var root = body.RootElement;
        Assert.Equal(102601, root.GetProperty("amount").GetInt64());
        Assert.Equal("EGP", root.GetProperty("currency").GetString());
        Assert.Equal(4242, root.GetProperty("payment_methods")[0].GetInt32());
        Assert.Equal("11111111222233334444555555555555", root.GetProperty("special_reference").GetString());
        Assert.Equal(102601, root.GetProperty("items").EnumerateArray().Sum(i => i.GetProperty("amount").GetInt64()));
        Assert.Equal("01000000000", root.GetProperty("billing_data").GetProperty("phone_number").GetString());
    }

    [Fact]
    public async Task BuildsCheckoutUrl_WithEscapedKeys()
    {
        var handler = new StubHandler((_, _) => Task.FromResult(Json(HttpStatusCode.Created,
            """{"id":"pi_1","client_secret":"cs/a&b"}""")));

        var session = await Gateway(handler).CreateCheckoutAsync(Request, CancellationToken.None);

        Assert.Equal("pi_1", session.ProviderReference);
        Assert.Equal("https://checkout.example.test/?publicKey=pk%20test%2B1&clientSecret=cs%2Fa%26b",
            session.CheckoutUrl.AbsoluteUri);
    }

    [Fact]
    public async Task SlowProvider_ThrowsUnavailable_AfterTheTimeout_NotLater()
    {
        var handler = new StubHandler(async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            return Json(HttpStatusCode.Created, "{}");
        });
        var started = DateTime.UtcNow;

        await Assert.ThrowsAsync<PaymentProviderUnavailableException>(
            () => Gateway(handler, timeoutSeconds: 1).CreateCheckoutAsync(Request, CancellationToken.None));

        Assert.InRange(DateTime.UtcNow - started, TimeSpan.FromSeconds(0.9), TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task CallerCancellation_IsNotReportedAsProviderOutage()
    {
        var handler = new StubHandler(async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            return Json(HttpStatusCode.Created, "{}");
        });
        using var caller = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        var ex = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Gateway(handler).CreateCheckoutAsync(Request, caller.Token));

        Assert.IsNotType<PaymentProviderUnavailableException>(ex);
    }
    
    [Fact]
    public async Task Inquiry_GetsAuthToken_ThenReadsTheTransactionWithBearer()
    {
        var seen = new List<(HttpMethod Method, string Path, string? Auth)>();
        var handler = new StubHandler((req, _) =>
        {
            seen.Add((req.Method, req.RequestUri!.AbsolutePath, req.Headers.Authorization?.ToString()));
            return Task.FromResult(req.RequestUri!.AbsolutePath == "/api/auth/tokens"
                ? Json(HttpStatusCode.Created, """{"token":"auth_abc"}""")
                : Json(HttpStatusCode.OK, """{"id":123,"is_voided":false,"refunded_amount_cents":11400}"""));
        });

        var state = await Gateway(handler).GetTransactionAsync("123", CancellationToken.None);

        Assert.Equal(new TransactionState(false, 11400), state);
        Assert.Equal((HttpMethod.Post, "/api/auth/tokens", (string?)null), seen[0]);
        Assert.Equal((HttpMethod.Get, "/api/acceptance/transactions/123", "Bearer auth_abc"), seen[1]);
    }

    [Fact]
    public async Task Inquiry_NotFound_IsTreatedAsUnknown_NotAsNothingHappened()
    {
        var handler = new StubHandler((req, _) => Task.FromResult(req.RequestUri!.AbsolutePath == "/api/auth/tokens"
            ? Json(HttpStatusCode.Created, """{"token":"auth_abc"}""")
            : Json(HttpStatusCode.NotFound, """{"detail":"Not found."}""")));

        await Assert.ThrowsAsync<PaymentProviderUnavailableException>(
            () => Gateway(handler).GetTransactionAsync("123", CancellationToken.None));
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.BadGateway)]
    public async Task ProviderServerError_IsUnavailable(HttpStatusCode status)
    {
        var handler = new StubHandler((_, _) => Task.FromResult(Json(status, "{}")));

        await Assert.ThrowsAsync<PaymentProviderUnavailableException>(
            () => Gateway(handler).CreateCheckoutAsync(Request, CancellationToken.None));
    }

    [Fact]
    public async Task ProviderRejectsRequest_IsABug_NotAnOutage()
    {
        var handler = new StubHandler((_, _) => Task.FromResult(Json(HttpStatusCode.BadRequest,
            """{"detail":"invalid integration"}""")));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Gateway(handler).CreateCheckoutAsync(Request, CancellationToken.None));
    }

    [Fact]
    public async Task NetworkFailure_IsUnavailable()
    {
        var handler = new StubHandler((_, _) => throw new HttpRequestException("connection refused"));

        await Assert.ThrowsAsync<PaymentProviderUnavailableException>(
            () => Gateway(handler).CreateCheckoutAsync(Request, CancellationToken.None));
    }
}