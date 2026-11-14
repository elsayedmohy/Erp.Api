using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ErpDashboard.Api.Common;
using ErpDashboard.Api.Data;
using ErpDashboard.Api.Data.Entities;
using ErpDashboard.Api.Data.Enums;
using ErpDashboard.Api.Data.Seed;
using ErpDashboard.Api.Features.Inventory.Stock;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ErpDashboard.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class OrderLifecycleTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task NewOrder_IsDraft_AndHappyPathReachesDelivered()
    {
        var manager = await TokenAsync(RoleIds.Manager);
        var (orderId, _) = await CreateOrderAsync(manager, discount: 0m);

        Assert.Equal(OrderStatus.Draft, await StatusAsync(orderId));
        await OrderFlow.AdvanceAsync(_client, manager, orderId, "Delivered");
        Assert.Equal(OrderStatus.Delivered, await StatusAsync(orderId));
    }

    [Theory]
    [InlineData(20, "Confirmed")]         // 10% of 200: not above the threshold
    [InlineData(20.02, "PendingApproval")] // just above 10%
    public async Task Submit_RoutesByDiscount(decimal discount, string expected)
    {
        var manager = await TokenAsync(RoleIds.Manager);
        var (orderId, _) = await CreateOrderAsync(manager, discount);

        var response = await OrderFlow.SubmitAsync(_client, manager, orderId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(expected, (await ReadJsonAsync(response)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Approve_MovesStraightToConfirmed()
    {
        var creator = await TokenAsync(RoleIds.Manager);
        var reviewer = await TokenAsync(RoleIds.Manager);
        var (orderId, _) = await CreateOrderAsync(creator, discount: 180m);
        await OrderFlow.SubmitAsync(_client, creator, orderId);

        var response = await SendAsync(HttpMethod.Post, $"/api/orders/{orderId}/approve", reviewer);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(OrderStatus.Confirmed, await StatusAsync(orderId));
    }

    [Fact]
    public async Task Reject_CancelsAndReleasesReservation()
    {
        var creator = await TokenAsync(RoleIds.Manager);
        var reviewer = await TokenAsync(RoleIds.Manager);
        var (orderId, productId) = await CreateOrderAsync(creator, discount: 180m);
        await OrderFlow.SubmitAsync(_client, creator, orderId);

        await SendAsync(HttpMethod.Post, $"/api/orders/{orderId}/reject", reviewer, new { reason = "Too much" });

        Assert.Equal(OrderStatus.Cancelled, await StatusAsync(orderId));
        Assert.Equal(0, await ReservedAsync(productId));
    }

    [Theory]
    [InlineData("Draft")]
    [InlineData("Confirmed")]
    [InlineData("Preparing")]
    public async Task Cancel_BeforeShipping_ReleasesReservation_AndRecordsReason(string reachedStatus)
    {
        var manager = await TokenAsync(RoleIds.Manager);
        var (orderId, productId) = await CreateOrderAsync(manager, discount: 0m);
        if (reachedStatus != "Draft")
            await OrderFlow.AdvanceAsync(_client, manager, orderId, reachedStatus);

        var response = await OrderFlow.CancelAsync(_client, manager, orderId, "Showroom closed");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, await ReservedAsync(productId));
        var last = (await HistoryAsync(manager, orderId)).Last();
        Assert.Equal("Cancelled", last.GetProperty("toStatus").GetString());
        Assert.Equal("Showroom closed", last.GetProperty("reason").GetString());
        await AssertReservationReconciledAsync(productId);
    }

    [Fact]
    public async Task Cancel_PendingApproval_ReleasesReservation()
    {
        var manager = await TokenAsync(RoleIds.Manager);
        var (orderId, productId) = await CreateOrderAsync(manager, discount: 180m);
        await OrderFlow.SubmitAsync(_client, manager, orderId);

        Assert.Equal(HttpStatusCode.OK, (await OrderFlow.CancelAsync(_client, manager, orderId)).StatusCode);
        Assert.Equal(0, await ReservedAsync(productId));
    }

    [Theory]
    [InlineData("Shipped")]
    [InlineData("Delivered")]
    public async Task Cancel_AfterShipping_Returns409_WithAllowedTransitions(string reachedStatus)
    {
        var manager = await TokenAsync(RoleIds.Manager);
        var (orderId, _) = await CreateOrderAsync(manager, discount: 0m);
        await OrderFlow.AdvanceAsync(_client, manager, orderId, reachedStatus);

        var response = await OrderFlow.CancelAsync(_client, manager, orderId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await ReadJsonAsync(response);
        Assert.Equal("invalid_transition", problem.GetProperty("code").GetString());
        Assert.Equal(reachedStatus, problem.GetProperty("currentStatus").GetString());
    }

    [Theory]
    [InlineData("Shipped")]   // skipping Confirmed and Preparing
    [InlineData("Delivered")]
    [InlineData("Confirmed")] // must go through submit
    [InlineData("Cancelled")] // must go through cancel
    public async Task Draft_ToAnythingViaStatusEndpoint_Returns409(string target)
    {
        var manager = await TokenAsync(RoleIds.Manager);
        var (orderId, _) = await CreateOrderAsync(manager, discount: 0m);

        var response = await OrderFlow.SetStatusAsync(_client, manager, orderId, target);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("invalid_transition", (await ReadJsonAsync(response)).GetProperty("code").GetString());
        Assert.Equal(OrderStatus.Draft, await StatusAsync(orderId));
    }

    [Fact]
    public async Task Cancel_WithoutReason_Returns400()
    {
        var manager = await TokenAsync(RoleIds.Manager);
        var (orderId, _) = await CreateOrderAsync(manager, discount: 0m);

        var response = await SendAsync(HttpMethod.Post, $"/api/orders/{orderId}/cancel", manager, new { reason = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DeleteEndpoint_IsGone()
    {
        var manager = await TokenAsync(RoleIds.Manager);
        var (orderId, _) = await CreateOrderAsync(manager, discount: 0m);

        var response = await SendAsync(HttpMethod.Delete, $"/api/orders/{orderId}", manager);

        Assert.True(response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed);
        Assert.Equal(OrderStatus.Draft, await StatusAsync(orderId));
    }

    // ---------- helpers ----------

    private async Task AssertReservationReconciledAsync(Guid productId)
    {
        using var scope = factory.Services.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<IStockReconciliationService>()
            .ReconcileAsync(CancellationToken.None);
        Assert.DoesNotContain(result.ReservationMismatches, m => m.ProductId == productId);
    }

    private async Task<List<JsonElement>> HistoryAsync(string token, Guid orderId)
    {
        var response = await SendAsync(HttpMethod.Get, $"/api/orders/{orderId}/history", token);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.EnumerateArray().Select(e => e.Clone()).ToList();
    }

    private async Task<OrderStatus> StatusAsync(Guid orderId)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Orders
            .IgnoreQueryFilters([QueryFilters.Showroom])
            .Where(o => o.Id == orderId).Select(o => o.Status).SingleAsync();
    }

    private async Task<int> ReservedAsync(Guid productId)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().WarehouseStocks
            .Where(s => s.WarehouseId == WarehouseIds.Main && s.ProductId == productId)
            .Select(s => s.Reserved).SingleAsync();
    }

    private async Task<(Guid OrderId, Guid ProductId)> CreateOrderAsync(string token, decimal discount)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var category = new Category { Id = Guid.NewGuid(), Name = $"Cat-{suffix}" };
        var product = new Product
        {
            Id = Guid.NewGuid(), CategoryId = category.Id, Name = $"Product-{suffix}",
            Sku = $"SKU-{suffix}".ToUpperInvariant(), UnitPrice = 100m, CostPrice = 50m
        };
        var showroom = new Showroom
        {
            Id = Guid.NewGuid(), Name = $"Showroom-{suffix}", Email = $"showroom-{suffix}@tests.local",
            Address = "Test address", City = "Cairo", Country = "Egypt",
            PhoneNumber = "01000000000", TaxId = $"TAX-{suffix[..12]}"
        };
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.AddRange(category, product, showroom);
            db.WarehouseStocks.Add(new WarehouseStock { WarehouseId = WarehouseIds.Main, ProductId = product.Id, Quantity = 10 });
            db.StockMovements.Add(new StockMovement
            {
                Id = Guid.NewGuid(), WarehouseId = WarehouseIds.Main, ProductId = product.Id,
                Type = StockMovementType.OpeningBalance, Quantity = 10, CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var response = await SendAsync(HttpMethod.Post, "/api/orders", token, new
        {
            showroomId = showroom.Id,
            warehouseId = WarehouseIds.Main,
            paymentMethod = Enum.GetValues<PaymentMethod>()[0].ToString(),
            shippingAddress = "Showroom address",
            lines = new[] { new { productId = product.Id, quantity = 2, discount } }
        });
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return ((await ReadJsonAsync(response)).GetProperty("id").GetGuid(), product.Id);
    }

    private async Task<string> TokenAsync(Guid roleId)
    {
        var user = await factory.CreateUserAsync(roleId);
        var response = await _client.PostAsJsonAsync("/api/auth/login", new { email = user.Email, password = user.Password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await ReadJsonAsync(response)).GetProperty("token").GetString()!;
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.Clone();
    }

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
            request.Content = JsonContent.Create(body);
        return _client.SendAsync(request);
    }
}