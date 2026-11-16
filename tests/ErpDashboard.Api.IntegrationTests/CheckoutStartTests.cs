using System.Net;
using System.Text.Json;
using ErpDashboard.Api.Data.Enums;
using ErpDashboard.Api.Data.Seed;
using Microsoft.EntityFrameworkCore;


namespace ErpDashboard.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class CheckoutStartTests(ApiFactory factory) : IDisposable
{
    private readonly HttpClient _client = factory.CreateClient();
    private CheckoutTestKit Kit => new(factory, _client);
    public void Dispose() => factory.Payments.Reset();

    [Fact]
    public async Task ConfirmedOrder_ReturnsUrl_AndChargesGoodsWithTaxPlusVehicles()
    {
        var s = await Kit.ScenarioAsync();                                  // 2 × 100, no discount
        var a = await Kit.CarrierWithRateAsync(s.Manager, "Cairo", 500m);
        var b = await Kit.CarrierWithRateAsync(s.Manager, "Cairo", 300m);
        await OrderFlow.SubmitAsync(_client, s.Sales, s.OrderId);       // Confirmed

        var response = await Kit.CheckoutAsync(s.ShowroomUser, s.OrderId, a, b);

        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var body = await Kit.ReadJsonAsync(response);
        Assert.Equal("AwaitingPayment", body.GetProperty("outcome").GetString());
        var checkout = body.GetProperty("checkout");
        Assert.Equal(228.00m, checkout.GetProperty("goodsAmount").GetDecimal());     // 200 + 14%
        Assert.Equal(800.00m, checkout.GetProperty("shippingAmount").GetDecimal());  // no tax
        Assert.Equal(1028.00m, checkout.GetProperty("totalAmount").GetDecimal());
        Assert.StartsWith("https://", checkout.GetProperty("checkoutUrl").GetString());

        var request = Assert.Single(factory.Payments.Requests);
        Assert.Equal(102800, request.AmountMinor);
        var attempt = await Kit.Db(db => db.PaymentAttempts.SingleAsync(x => x.Id == request.AttemptId));
        Assert.Equal(s.OrderId, attempt.OrderId);
        Assert.Null(attempt.InvoiceId);
    }

    [Fact]
    public async Task DraftOrder_WithoutBigDiscount_IsSubmittedAndCheckedOut()
    {
        var s = await Kit.ScenarioAsync();
        var carrier = await Kit.CarrierWithRateAsync(s.Manager, "Cairo", 500m);

        var response = await Kit.CheckoutAsync(s.Sales, s.OrderId, carrier);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("AwaitingPayment", (await Kit.ReadJsonAsync(response)).GetProperty("outcome").GetString());
        Assert.Equal(OrderStatus.Confirmed, await Kit.StatusAsync(s.OrderId));
    }

    [Fact]
    public async Task DraftOrder_NeedingApproval_GoesToApproval_WithoutPayment()
    {
        var s = await Kit.ScenarioAsync(discountPercent: 15);
        var carrier = await Kit.CarrierWithRateAsync(s.Manager, "Cairo", 500m);

        var response = await Kit.CheckoutAsync(s.Sales, s.OrderId, carrier);

        var body = await Kit.ReadJsonAsync(response);
        Assert.Equal("AwaitingApproval", body.GetProperty("outcome").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("checkout").ValueKind);
        Assert.Equal(OrderStatus.PendingApproval, await Kit.StatusAsync(s.OrderId));
        Assert.Empty(factory.Payments.Requests);
    }

    [Fact]
    public async Task UnpricedCarrier_Returns400_AndOrderStaysDraft()
    {
        var s = await Kit.ScenarioAsync();
        var wrong = await Kit.CarrierWithRateAsync(s.Manager, "Aswan", 500m);

        var response = await Kit.CheckoutAsync(s.Sales, s.OrderId, wrong);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("vehicles[0].carrierId", await response.Content.ReadAsStringAsync());
        Assert.Equal(OrderStatus.Draft, await Kit.StatusAsync(s.OrderId));
    }

    [Fact]
    public async Task SecondCheckout_WhileFirstAwaitsPayment_Returns409()
    {
        var s = await Kit.ScenarioAsync();
        var carrier = await Kit.CarrierWithRateAsync(s.Manager, "Cairo", 500m);
        await Kit.CheckoutAsync(s.Sales, s.OrderId, carrier);

        var second = await Kit.CheckoutAsync(s.Sales, s.OrderId, carrier);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("checkout_in_progress", await Kit.CodeAsync(second));
    }

    [Fact]
    public async Task ProviderDown_Returns504_CheckoutFailed_ThenRetryWorks()
    {
        var s = await Kit.ScenarioAsync();
        var carrier = await Kit.CarrierWithRateAsync(s.Manager, "Cairo", 500m);
        factory.Payments.Unavailable = true;

        var failed = await Kit.CheckoutAsync(s.Sales, s.OrderId, carrier);
        factory.Payments.Unavailable = false;
        var retry = await Kit.CheckoutAsync(s.Sales, s.OrderId, carrier);

        Assert.Equal(HttpStatusCode.GatewayTimeout, failed.StatusCode);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        var statuses = await Kit.Db(db => db.OrderCheckouts.Where(c => c.OrderId == s.OrderId)
            .OrderBy(c => c.CreatedAt).Select(c => c.Status).ToListAsync());
        Assert.Equal([CheckoutStatus.Failed, CheckoutStatus.AwaitingPayment], statuses);
    }

    [Fact]
    public async Task StaleVersion_Returns409_AndMissingVersion_Returns428()
    {
        var s = await Kit.ScenarioAsync();
        var carrier = await Kit.CarrierWithRateAsync(s.Manager, "Cairo", 500m);
        var body = new { vehicles = new[] { new { carrierId = carrier } } };

        var stale = await OrderFlow.SendAsync(_client, HttpMethod.Post, $"/api/orders/{s.OrderId}/checkout", s.Sales, version: 1, body);
        var missing = await OrderFlow.SendAsync(_client, HttpMethod.Post, $"/api/orders/{s.OrderId}/checkout", s.Sales, version: null, body);

        Assert.Equal("order_changed", await Kit.CodeAsync(stale));
        Assert.Equal((HttpStatusCode)428, missing.StatusCode);
    }

    [Fact]
    public async Task ShowroomUser_OfAnotherShowroom_Gets404()
    {
        var s = await Kit.ScenarioAsync();
        var other = await Kit.ScenarioAsync();
        var carrier = await Kit.CarrierWithRateAsync(s.Manager, "Cairo", 500m);

        var response = await OrderFlow.SendAsync(_client, HttpMethod.Post, $"/api/orders/{s.OrderId}/checkout",
            other.ShowroomUser, version: 1, new { vehicles = new[] { new { carrierId = carrier } } });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task InventoryManager_CannotCheckout()
    {
        var s = await Kit.ScenarioAsync();
        var inventory = await Kit.TokenAsync(RoleIds.InventoryManager);

        var response = await OrderFlow.SendAsync(_client, HttpMethod.Post, $"/api/orders/{s.OrderId}/checkout",
            inventory, version: 1, new { vehicles = new[] { new { carrierId = CarrierIds.CompanyFleet } } });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ShowroomUser_SeesQuotes_ButNotCarrierPhones()
    {
        var s = await Kit.ScenarioAsync();

        var quotes = await OrderFlow.SendAsync(_client, HttpMethod.Get, "/api/shipping-quotes?governorate=Cairo", s.ShowroomUser, version: null);
        var carriers = await OrderFlow.SendAsync(_client, HttpMethod.Get, "/api/carriers", s.ShowroomUser, version: null);

        Assert.Equal(HttpStatusCode.OK, quotes.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, carriers.StatusCode);
    }

    [Fact]
    public async Task Get_ReturnsLatestCheckout_ForTheShowroomUser()
    {
        var s = await Kit.ScenarioAsync();
        var carrier = await Kit.CarrierWithRateAsync(s.Manager, "Cairo", 500m);
        await Kit.CheckoutAsync(s.Sales, s.OrderId, carrier);

        var response = await OrderFlow.SendAsync(_client, HttpMethod.Get, $"/api/orders/{s.OrderId}/checkout", s.ShowroomUser, version: null);

        var body = await Kit.ReadJsonAsync(response);
        Assert.Equal("AwaitingPayment", body.GetProperty("status").GetString());
        Assert.Single(body.GetProperty("vehicles").EnumerateArray());
    }


    
}