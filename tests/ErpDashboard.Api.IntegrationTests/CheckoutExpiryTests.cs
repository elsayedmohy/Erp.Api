using ErpDashboard.Api.Data;
using ErpDashboard.Api.Data.Entities;
using ErpDashboard.Api.Data.Enums;
using ErpDashboard.Api.Data.Seed;
using ErpDashboard.Api.Features.Sales.Checkout;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ErpDashboard.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class CheckoutExpiryTests(ApiFactory factory) : IDisposable
{
    private readonly HttpClient _client = factory.CreateClient();
    private CheckoutTestKit Kit => new(factory, _client);

    public void Dispose()
    {
        factory.Payments.Reset();
        factory.CancellationFaults.FailFor.Clear();
    }

    [Fact]
    public async Task Expired_CancelsOrder_ReleasesStock_AndExplains()
    {
        var (s, checkoutId) = await StartedCheckoutAsync();
        Assert.Equal(2, await ReservedAsync(s.ProductId));

        await ExpireAsync(checkoutId);
        await DrainAsync();

        Assert.Equal(OrderStatus.Cancelled, await Kit.StatusAsync(s.OrderId));
        Assert.Equal(0, await ReservedAsync(s.ProductId));
        var checkout = await CheckoutAsync(checkoutId);
        Assert.Equal(CheckoutStatus.Expired, checkout.Status);
        Assert.Equal(CheckoutExpiryProcessor.ExpiredMessage, checkout.CustomerMessage);
        var attempt = await Kit.Db(db => db.PaymentAttempts.SingleAsync(a => a.Id == checkout.PaymentAttemptId));
        Assert.Equal(PaymentAttemptStatus.Failed, attempt.Status);
        var history = await Kit.Db(db => db.OrderStatusChanges
            .Where(h => h.OrderId == s.OrderId && h.ToStatus == OrderStatus.Cancelled).ToListAsync());
        var row = Assert.Single(history);
        Assert.Null(row.ChangedById);
        Assert.Equal("Payment window expired.", row.Reason);
    }

    [Fact]
    public async Task NotYetExpired_IsLeftAlone()
    {
        var (s, checkoutId) = await StartedCheckoutAsync();

        await DrainAsync();

        Assert.Equal(CheckoutStatus.AwaitingPayment, (await CheckoutAsync(checkoutId)).Status);
        Assert.Equal(OrderStatus.Confirmed, await Kit.StatusAsync(s.OrderId));
    }

    [Fact]
    public async Task OrderCancelledByHand_CheckoutStillExpires_StockReleasedOnce()
    {
        var (s, checkoutId) = await StartedCheckoutAsync();
        await OrderFlow.CancelAsync(_client, s.Manager, s.OrderId);
        Assert.Equal(0, await ReservedAsync(s.ProductId));

        await ExpireAsync(checkoutId);
        await DrainAsync();

        Assert.Equal(CheckoutStatus.Expired, (await CheckoutAsync(checkoutId)).Status);
        Assert.Equal(0, await ReservedAsync(s.ProductId));      // not −2
    }

    [Fact]
    public async Task AfterExpiry_OrderCannotBeCheckedOutAgain()
    {
        var (s, checkoutId) = await StartedCheckoutAsync();
        await ExpireAsync(checkoutId);
        await DrainAsync();

        var again = await Kit.CheckoutAsync(s.Sales, s.OrderId, s.CarrierId);

        Assert.Equal("not_checkoutable", await Kit.CodeAsync(again));
    }

    [Fact]
    public async Task TwoWorkersAtOnce_CancelOnce()
    {
        var (s, checkoutId) = await StartedCheckoutAsync();
        await ExpireAsync(checkoutId);

        await Task.WhenAll(DrainAsync(), DrainAsync());

        Assert.Equal(0, await ReservedAsync(s.ProductId));
        Assert.Equal(1, await Kit.Db(db => db.OrderStatusChanges
            .CountAsync(h => h.OrderId == s.OrderId && h.ToStatus == OrderStatus.Cancelled)));
    }

    [Fact]
    public async Task FailingCheckout_RollsBack_AndDoesNotBlockOthers()
    {
        var (bad, badId) = await StartedCheckoutAsync();
        var (good, goodId) = await StartedCheckoutAsync();
        factory.CancellationFaults.FailFor[bad.OrderId] = 0;
        await ExpireAsync(badId, minutesAgo: 10);          // older: claimed first
        await ExpireAsync(goodId, minutesAgo: 1);

        await DrainAsync();

        var failed = await CheckoutAsync(badId);
        Assert.Equal(CheckoutStatus.AwaitingPayment, failed.Status);
        Assert.True(failed.ExpiryRetryAt > DateTime.UtcNow);
        Assert.Equal(OrderStatus.Confirmed, await Kit.StatusAsync(bad.OrderId));     // cancel rolled back
        Assert.Equal(2, await ReservedAsync(bad.ProductId));
        Assert.Equal(CheckoutStatus.Expired, (await CheckoutAsync(goodId)).Status);  // not blocked

        factory.CancellationFaults.FailFor.Clear();
        await Kit.Db(db => db.OrderCheckouts.Where(c => c.Id == badId)
            .ExecuteUpdateAsync(x => x.SetProperty(c => c.ExpiryRetryAt, DateTime.UtcNow.AddSeconds(-1))));
        await DrainAsync();
        Assert.Equal(CheckoutStatus.Expired, (await CheckoutAsync(badId)).Status);
    }

    // ---------- helpers ----------

    private async Task<(CheckoutTestKit.Scenario Scenario, Guid CheckoutId)> StartedCheckoutAsync()
    {
        var s = await Kit.ScenarioAsync();
        var response = await Kit.CheckoutAsync(s.Sales, s.OrderId, s.CarrierId);
        var body = await Kit.ReadJsonAsync(response);
        return (s, body.GetProperty("checkout").GetProperty("id").GetGuid());
    }

    private Task<int> ExpireAsync(Guid checkoutId, int minutesAgo = 1) =>
        Kit.Db(db => db.OrderCheckouts.Where(c => c.Id == checkoutId)
            .ExecuteUpdateAsync(x => x.SetProperty(c => c.ExpiresAt, DateTime.UtcNow.AddMinutes(-minutesAgo))));

    private Task<OrderCheckout> CheckoutAsync(Guid id) =>
        Kit.Db(db => db.OrderCheckouts.AsNoTracking().SingleAsync(c => c.Id == id));

    private Task<int> ReservedAsync(Guid productId) =>
        Kit.Db(db => db.WarehouseStocks
            .Where(w => w.WarehouseId == WarehouseIds.Main && w.ProductId == productId)
            .Select(w => w.Reserved).SingleAsync());

    private async Task DrainAsync()
    {
        for (var i = 0; i < 1000; i++)
        {
            using var scope = factory.Services.CreateScope();
            if (!await scope.ServiceProvider.GetRequiredService<CheckoutExpiryProcessor>()
                    .ProcessNextAsync(CancellationToken.None))
                return;
        }
        Assert.Fail("Expiry queue did not drain.");
    }
}