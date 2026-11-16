using System.Net;
using System.Text;
using ErpDashboard.Api.Common;
using ErpDashboard.Api.Data.Entities;
using ErpDashboard.Api.Data.Enums;
using ErpDashboard.Api.Data.Seed;
using ErpDashboard.Api.Features.Payments.Webhooks;
using ErpDashboard.Api.Features.Sales.Checkout;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ErpDashboard.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class CheckoutCompletionTests(ApiFactory factory) : IDisposable
{
    private readonly HttpClient _client = factory.CreateClient();
    private CheckoutTestKit Kit => new(factory, _client);

    public void Dispose()
    {
        factory.Payments.Reset();
        factory.InvoiceFaults.FailFor.Clear();
    }

    [Fact]
    public async Task Paid_BooksShipment_IssuesInvoice_RecordsPayment_AllAtOnce()
    {
        var c = await StartedAsync();

        await PayAsync(c);

        var shipment = await Kit.Db(db => db.Shipments.Include(s => s.Vehicles).SingleAsync(s => s.OrderId == c.OrderId));
        Assert.Equal(CheckoutTestKit.CairoVehiclePrice, Assert.Single(shipment.Vehicles).Price);
        Assert.Null(shipment.BookedById);

        var invoice = await Kit.Db(db => Invoices(db).Include(i => i.Payments).Include(i => i.InvoiceLines)
            .SingleAsync(i => i.OrderId == c.OrderId));
        Assert.Equal(728.00m, invoice.TotalAmount);                    // 228 goods + 500 shipping
        var payment = Assert.Single(invoice.Payments);
        Assert.Equal(PaymentMethod.Online, payment.Method);
        Assert.Equal(728.00m, payment.Amount);
        Assert.Contains(invoice.InvoiceLines, l => l.Description.StartsWith("Shipping") && l.TaxRate == 0);

        var checkout = await CheckoutAsync(c.CheckoutId);
        Assert.Equal(CheckoutStatus.Completed, checkout.Status);
        Assert.Contains(shipment.TrackingNumber, checkout.CustomerMessage);
        Assert.Equal(PaymentAttemptStatus.Succeeded, (await AttemptAsync(c.AttemptId)).Status);
        Assert.Equal(OrderStatus.Confirmed, await Kit.StatusAsync(c.OrderId));
        Assert.Equal(2, await ReservedAsync(c.ProductId));             // still reserved until Shipped
    }

    [Fact]
    public async Task SameCallbackTwice_CompletesOnce()
    {
        var c = await StartedAsync();
        var transactionId = Random.Shared.NextInt64(1, long.MaxValue);

        await PostAsync(c, transactionId: transactionId);
        await PostAsync(c, transactionId: transactionId);
        await DrainWebhooksAsync();

        Assert.Equal(1, await Kit.Db(db => db.Shipments.CountAsync(s => s.OrderId == c.OrderId)));
        Assert.Equal(1, await Kit.Db(db => Invoices(db).CountAsync(i => i.OrderId == c.OrderId)));
        Assert.Equal(1, await Kit.Db(db => db.InvoicePayments.CountAsync(p => p.PaymentAttemptId == c.AttemptId)));
    }

    [Fact]
    public async Task Declined_LeavesCheckoutOpen_AndChangesNothingElse()
    {
        var c = await StartedAsync();

        await PayAsync(c, success: false);

        Assert.Equal(PaymentAttemptStatus.Failed, (await AttemptAsync(c.AttemptId)).Status);
        Assert.Equal(CheckoutStatus.AwaitingPayment, (await CheckoutAsync(c.CheckoutId)).Status);
        Assert.Equal(OrderStatus.Confirmed, await Kit.StatusAsync(c.OrderId));
        Assert.False(await Kit.Db(db => db.Shipments.AnyAsync(s => s.OrderId == c.OrderId)));
    }

    // ----- failure points: shipping fails after payment -----

    [Fact]
    public async Task CarrierDeactivatedWhilePaying_Compensates()
    {
        var c = await StartedAsync();
        await Kit.Db(db => db.Carriers.Where(x => x.Id == c.CarrierId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false)));

        await PayAsync(c);

        await AssertCompensatedAsync(c);
    }

    [Fact]
    public async Task OrderCancelledByHandWhilePaying_Compensates_StockReleasedOnce()
    {
        var c = await StartedAsync();
        await OrderFlow.CancelAsync(_client, c.Scenario.Manager, c.OrderId);

        await PayAsync(c);

        await AssertCompensatedAsync(c);
    }

    [Fact]
    public async Task BusinessFailureAfterShipmentBooked_RollsBackShipment_AndCompensates()
    {
        var c = await StartedAsync();
        factory.InvoiceFaults.FailFor[c.OrderId] = InjectedFailure.Business;

        await PayAsync(c);

        await AssertCompensatedAsync(c);
    }

    [Fact]
    public async Task WrongAmount_Compensates()
    {
        var c = await StartedAsync();

        await PayAsync(c, amountMinor: c.AmountMinor - 100);

        await AssertCompensatedAsync(c);
    }

    [Fact]
    public async Task PaymentAfterExpiry_KeepsExpired_AndAsksForRefund()
    {
        var c = await StartedAsync();
        await Kit.Db(db => db.OrderCheckouts.Where(x => x.Id == c.CheckoutId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ExpiresAt, DateTime.UtcNow.AddMinutes(-1))));
        await DrainExpiryAsync();

        await PayAsync(c);

        var checkout = await CheckoutAsync(c.CheckoutId);
        Assert.Equal(CheckoutStatus.Expired, checkout.Status);
        Assert.Equal(CheckoutCompletion.RefundPendingMessage, checkout.CustomerMessage);
        Assert.Equal(PaymentAttemptStatus.NeedsRefund, (await AttemptAsync(c.AttemptId)).Status);
        Assert.Equal(0, await ReservedAsync(c.ProductId));
    }

    // ----- technical failure: retry, never compensate -----

    [Fact]
    public async Task TechnicalFailure_RollsBackEverything_RetriesLater_ThenCompletes()
    {
        var c = await StartedAsync();
        factory.InvoiceFaults.FailFor[c.OrderId] = InjectedFailure.Technical;

        var eventId = await PostAsync(c);
        await DrainWebhooksAsync();

        var e = await Kit.Db(db => db.PaymentWebhookEvents.SingleAsync(x => x.Id == eventId));
        Assert.Equal(WebhookEventStatus.Received, e.Status);
        Assert.Equal(1, e.ProcessingAttempts);
        Assert.False(await Kit.Db(db => db.Shipments.AnyAsync(s => s.OrderId == c.OrderId)));   // rolled back
        Assert.False(await Kit.Db(db => Invoices(db).AnyAsync(i => i.OrderId == c.OrderId)));
        Assert.Equal(OrderStatus.Confirmed, await Kit.StatusAsync(c.OrderId));                // not compensated
        Assert.Equal(PaymentAttemptStatus.Pending, (await AttemptAsync(c.AttemptId)).Status);

        factory.InvoiceFaults.FailFor.Clear();
        await Kit.Db(db => db.PaymentWebhookEvents.Where(x => x.Id == eventId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.NextAttemptAt, DateTime.UtcNow.AddSeconds(-1))));
        await DrainWebhooksAsync();

        Assert.Equal(CheckoutStatus.Completed, (await CheckoutAsync(c.CheckoutId)).Status);
    }

    // ----- race: payment and expiry at the same moment -----

    [Fact]
    public async Task PaymentAndExpiryTogether_EndInOneConsistentState()
    {
        var c = await StartedAsync();
        await Kit.Db(db => db.OrderCheckouts.Where(x => x.Id == c.CheckoutId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ExpiresAt, DateTime.UtcNow.AddMinutes(-1))));
        await PostAsync(c);

        await Task.WhenAll(DrainWebhooksAsync(), DrainExpiryAsync());

        var checkout = await CheckoutAsync(c.CheckoutId);
        var attempt = await AttemptAsync(c.AttemptId);
        var invoiced = await Kit.Db(db => Invoices(db).AnyAsync(i => i.OrderId == c.OrderId));
        var status = await Kit.StatusAsync(c.OrderId);

        if (checkout.Status == CheckoutStatus.Completed)
        {
            Assert.Equal(PaymentAttemptStatus.Succeeded, attempt.Status);
            Assert.True(invoiced);
            Assert.Equal(OrderStatus.Confirmed, status);
        }
        else
        {
            Assert.Equal(CheckoutStatus.Expired, checkout.Status);
            Assert.Equal(PaymentAttemptStatus.NeedsRefund, attempt.Status);
            Assert.False(invoiced);
            Assert.Equal(OrderStatus.Cancelled, status);
            Assert.Equal(0, await ReservedAsync(c.ProductId));
        }
    }

    // ---------- helpers ----------

    private sealed record Started(
        CheckoutTestKit.Scenario Scenario, Guid CheckoutId, Guid AttemptId, long AmountMinor)
    {
        public Guid OrderId => Scenario.OrderId;
        public Guid ProductId => Scenario.ProductId;
        public Guid CarrierId => Scenario.CarrierId;
    }

    private async Task<Started> StartedAsync()
    {
        var s = await Kit.ScenarioAsync();
        var response = await Kit.CheckoutAsync(s.Sales, s.OrderId, s.CarrierId);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var checkoutId = (await Kit.ReadJsonAsync(response)).GetProperty("checkout").GetProperty("id").GetGuid();
        var checkout = await CheckoutAsync(checkoutId);
        var attempt = await AttemptAsync(checkout.PaymentAttemptId!.Value);
        return new Started(s, checkoutId, attempt.Id, attempt.AmountMinor);
    }

    private async Task PayAsync(Started c, bool success = true, long? amountMinor = null)
    {
        await PostAsync(c, success, amountMinor);
        await DrainWebhooksAsync();
    }

    private async Task<Guid> PostAsync(Started c, bool success = true, long? amountMinor = null, long? transactionId = null)
    {
        var payload = WebhookSigning.For(c.AttemptId, amountMinor ?? c.AmountMinor, success, transactionId: transactionId);
        var response = await _client.PostAsync($"/api/payments/webhook?hmac={WebhookSigning.Sign(payload)}",
            new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var id = payload["obj"]!["id"]!.ToString();
        return await Kit.Db(db => db.PaymentWebhookEvents.Where(x => x.ProviderEventId == id)
            .OrderByDescending(x => x.ReceivedAt).Select(x => x.Id).FirstAsync());
    }

    private async Task AssertCompensatedAsync(Started c)
    {
        Assert.Equal(OrderStatus.Cancelled, await Kit.StatusAsync(c.OrderId));
        Assert.Equal(0, await ReservedAsync(c.ProductId));
        Assert.False(await Kit.Db(db => db.Shipments.AnyAsync(s => s.OrderId == c.OrderId && s.Status == ShipmentStatus.Booked)));
        Assert.False(await Kit.Db(db => Invoices(db).AnyAsync(i => i.OrderId == c.OrderId)));
        Assert.Equal(PaymentAttemptStatus.NeedsRefund, (await AttemptAsync(c.AttemptId)).Status);
        var checkout = await CheckoutAsync(c.CheckoutId);
        Assert.Equal(CheckoutCompletion.RefundPendingMessage, checkout.CustomerMessage);
    }

    private Task<OrderCheckout> CheckoutAsync(Guid id) =>
        Kit.Db(db => db.OrderCheckouts.AsNoTracking().SingleAsync(x => x.Id == id));

    private Task<PaymentAttempt> AttemptAsync(Guid id) =>
        Kit.Db(db => db.PaymentAttempts.AsNoTracking().SingleAsync(x => x.Id == id));

    private Task<int> ReservedAsync(Guid productId) =>
        Kit.Db(db => db.WarehouseStocks.Where(w => w.WarehouseId == WarehouseIds.Main && w.ProductId == productId)
            .Select(w => w.Reserved).SingleAsync());

    private Task DrainWebhooksAsync() => DrainAsync<PaymentWebhookProcessor>();
    private Task DrainExpiryAsync() => DrainAsync<CheckoutExpiryProcessor>();

    private async Task DrainAsync<T>() where T : ErpDashboard.Api.Common.Workers.IQueueProcessor
    {
        for (var i = 0; i < 1000; i++)
        {
            using var scope = factory.Services.CreateScope();
            if (!await scope.ServiceProvider.GetRequiredService<T>().ProcessNextAsync(CancellationToken.None))
                return;
        }
        Assert.Fail($"{typeof(T).Name} queue did not drain.");
    }
    
    private static IQueryable<Invoice> Invoices(ErpDashboard.Api.Data.AppDbContext db) =>
       db.Invoices.IgnoreQueryFilters([QueryFilters.Showroom]);
}