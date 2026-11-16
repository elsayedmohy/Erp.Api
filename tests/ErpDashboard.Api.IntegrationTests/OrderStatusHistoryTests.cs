using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ErpDashboard.Api.Common;
using ErpDashboard.Api.Data;
using ErpDashboard.Api.Data.Entities;
using ErpDashboard.Api.Data.Enums;
using ErpDashboard.Api.Data.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ErpDashboard.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class OrderStatusHistoryTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task FullLifecycle_IsRecorded_InOrder_WithWho()
    {
        var (manager, managerId) = await LoginAsync(RoleIds.Manager);
        var orderId = await CreateOrderAsync(manager, discount: 0m);


        await OrderFlow.AdvanceAsync(_client, manager, orderId, "Shipped");

        var history = await HistoryAsync(manager, orderId);

        Assert.Equal(
            [(null, "Draft"), ("Draft", "Confirmed"), ("Confirmed", "Preparing"), ("Preparing", "Shipped")],
            history.Select(h => (From(h), h.GetProperty("toStatus").GetString())));
        Assert.All(history, h => Assert.Equal(managerId, h.GetProperty("changedById").GetGuid()));
        Assert.All(history, h => Assert.False(string.IsNullOrWhiteSpace(h.GetProperty("changedByName").GetString())));
    }

    [Fact]
    public async Task ApproveAndReject_RecordTheReviewer_AndRejectReason()
    {
        var (creator, _) = await LoginAsync(RoleIds.Manager);
        var (reviewer, reviewerId) = await LoginAsync(RoleIds.Manager);
        var approved = await CreateOrderAsync(creator, discount: 180m);
        var rejected = await CreateOrderAsync(creator, discount: 180m);
        await OrderFlow.SubmitAsync(_client, creator, approved);
        await OrderFlow.SubmitAsync(_client, creator, rejected);

        Assert.True((await OrderFlow.ApproveAsync(_client, reviewer, approved)).IsSuccessStatusCode);
        Assert.True((await OrderFlow.RejectAsync(_client, reviewer, rejected, "Too much discount")).IsSuccessStatusCode);

        var a = (await HistoryAsync(creator, approved)).Last();
        Assert.Equal(("PendingApproval", "Confirmed"), (From(a), a.GetProperty("toStatus").GetString()));
        Assert.Equal(reviewerId, a.GetProperty("changedById").GetGuid());

        var r = (await HistoryAsync(creator, rejected)).Last();
        Assert.Equal("Cancelled", r.GetProperty("toStatus").GetString());
        Assert.Equal("Rejected: Too much discount", r.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task InvalidTransition_RecordsNothing()
    {
        var (manager, _) = await LoginAsync(RoleIds.Manager);
        var orderId = await CreateOrderAsync(manager, discount: 0m);

        var response = await SetStatusAsync(manager, orderId, "Delivered");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Single(await HistoryAsync(manager, orderId));
    }

    [Fact]
    public async Task FailureDuringShipping_RollsBackStatusAndHistoryTogether()
    {
        var (manager, _) = await LoginAsync(RoleIds.Manager);
        var orderId = await CreateOrderAsync(manager, discount: 0m);
        await OrderFlow.AdvanceAsync(_client, manager, orderId, "Preparing");

        factory.StockFaults.FailAfterChange(1);
        HttpResponseMessage response;
        try { response = await SetStatusAsync(manager, orderId, "Shipped"); }
        finally { factory.StockFaults.Reset(); }

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var history = await HistoryAsync(manager, orderId);
        Assert.Equal("Preparing", history.Last().GetProperty("toStatus").GetString());
        Assert.Equal(OrderStatus.Preparing, await StatusAsync(orderId));
    }

    [Fact]
    public async Task ConcurrentShip_RecordsOneShippedRow()
    {
        var (manager, _) = await LoginAsync(RoleIds.Manager);
        var orderId = await CreateOrderAsync(manager, discount: 0m);
        await OrderFlow.AdvanceAsync(_client, manager, orderId, "Preparing");

        await Task.WhenAll(SetStatusAsync(manager, orderId, "Shipped"), SetStatusAsync(manager, orderId, "Shipped"));

        var history = await HistoryAsync(manager, orderId);
        Assert.Single(history, h => h.GetProperty("toStatus").GetString() == "Shipped");
    }

    // ---------- helpers ----------

    private static string? From(JsonElement h) =>
        h.GetProperty("fromStatus").ValueKind == JsonValueKind.Null ? null : h.GetProperty("fromStatus").GetString();

    private async Task<List<JsonElement>> HistoryAsync(string token, Guid orderId)
    {
        var response = await SendAsync(HttpMethod.Get, $"/api/orders/{orderId}/history", token);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.EnumerateArray().Select(e => e.Clone()).ToList();
    }

    private Task<HttpResponseMessage> SetStatusAsync(string token, Guid orderId, string status) =>
        OrderFlow.SetStatusAsync(_client, token, orderId, status);



    private async Task<OrderStatus> StatusAsync(Guid orderId)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Orders
            .IgnoreQueryFilters([QueryFilters.Showroom])
            .Where(o => o.Id == orderId).Select(o => o.Status).SingleAsync();
    }

    private async Task<Guid> CreateOrderAsync(string token, decimal discount)
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
            shippingGovernorate = "Cairo",
            lines = new[] { new { productId = product.Id, quantity = 2, discount } }
        });
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("id").GetGuid();
    }

    private async Task<(string Token, Guid UserId)> LoginAsync(Guid roleId)
    {
        var user = await factory.CreateUserAsync(roleId);
        var response = await _client.PostAsJsonAsync("/api/auth/login", new { email = user.Email, password = user.Password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return (json.RootElement.GetProperty("token").GetString()!, user.Id);
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