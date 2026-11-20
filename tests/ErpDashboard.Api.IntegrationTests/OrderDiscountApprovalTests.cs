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
public class OrderDiscountApprovalTests(ApiFactory factory)
{
    private const decimal UnitPrice = 100m;
    private const int Stock = 10;

    private readonly HttpClient _client = factory.CreateClient();

    [Theory]
    [InlineData(0, "Confirmed")]
    [InlineData(10, "Confirmed")]
    [InlineData(10.01, "PendingApproval")]
    public async Task Submit_SetsStatusByDiscount(decimal discount, string expected)
    {
        var (creator, _) = await ManagerAsync();
        var (showroomId, productId) = await SeedShowroomAndProductAsync();
        var created = await CreateOrderAsync(creator, showroomId, productId, discount);
        Assert.Equal("Draft", created.GetProperty("status").GetString());

        var response = await OrderFlow.SubmitAsync(_client, creator, created.GetProperty("id").GetGuid());

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(expected, json.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task PendingApproval_CannotBeConfirmed()
    {
        var orderId = await CreatePendingApprovalOrderAsync();
        var (manager, _) = await ManagerAsync();

        var response = await OrderFlow.SetStatusAsync(_client, manager, orderId, "Confirmed");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Creator_CannotApproveOwnOrder()
    {
        var (creator, _) = await ManagerAsync();
        var (showroomId, productId) = await SeedShowroomAndProductAsync();
        var orderId = await CreateAndSubmitAsync(creator, showroomId, productId, 50);

        var response = await OrderFlow.ApproveAsync(_client, creator, orderId);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(OrderStatus.PendingApproval, (await GetOrderAsync(orderId)).Status);
    }

    [Fact]
    public async Task UserWithoutApprovePermission_Gets403()
    {
        var orderId = await CreatePendingApprovalOrderAsync();
        var accountant = await LoginAsync(await factory.CreateUserAsync(RoleIds.Accountant));
        var (manager, _) = await ManagerAsync();
        // The accountant can't read the order, so the version comes from someone who can.
        var version = await OrderFlow.VersionAsync(_client, manager, orderId);

        var response = await OrderFlow.SendAsync(_client, HttpMethod.Post, $"/api/orders/{orderId}/approve", accountant, version);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task OtherManager_Approves_AndOrderIsConfirmed()
    {
        var orderId = await CreatePendingApprovalOrderAsync();
        var (reviewer, reviewerId) = await ManagerAsync();

        var approve = await OrderFlow.ApproveAsync(_client, reviewer, orderId);

        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);
        var order = await GetOrderAsync(orderId);
        Assert.Equal(OrderStatus.Confirmed, order.Status);
        Assert.Equal(reviewerId, order.ReviewedById);
        Assert.NotNull(order.ReviewedAt);
    }

    [Fact]
    public async Task Reject_ReleasesStock()
    {
        var (creator, _) = await ManagerAsync();
        var (showroomId, productId) = await SeedShowroomAndProductAsync();
        var orderId = await CreateAndSubmitAsync(creator, showroomId, productId, 50);
        var (reviewer, _) = await ManagerAsync();

        var response = await OrderFlow.RejectAsync(_client, reviewer, orderId, "Discount too high");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var order = await GetOrderAsync(orderId);
        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal("Discount too high", order.RejectionReason);
    }

    [Fact]
    public async Task Reject_WithoutReason_Returns400()
    {
        var orderId = await CreatePendingApprovalOrderAsync();
        var (reviewer, _) = await ManagerAsync();

        var response = await OrderFlow.SendAsync(_client, HttpMethod.Post, $"/api/orders/{orderId}/reject", reviewer,
            await OrderFlow.VersionAsync(_client, reviewer, orderId), new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ConcurrentApproveAndReject_OnlyOneWins()
    {
        var (creator, _) = await ManagerAsync();
        var (showroomId, productId) = await SeedShowroomAndProductAsync();
        var orderId = await CreateAndSubmitAsync(creator, showroomId, productId, 50);
        var (approver, _) = await ManagerAsync();
        var (rejecter, _) = await ManagerAsync();

        var version = await OrderFlow.VersionAsync(_client, approver, orderId);
        var responses = await Task.WhenAll(
            OrderFlow.SendAsync(_client, HttpMethod.Post, $"/api/orders/{orderId}/approve", approver, version),
            OrderFlow.SendAsync(_client, HttpMethod.Post, $"/api/orders/{orderId}/reject", rejecter, version, new { reason = "No" }));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        var conflict = Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
        using var json = JsonDocument.Parse(await conflict.Content.ReadAsStringAsync());
        Assert.Equal("order_changed", json.RootElement.GetProperty("code").GetString());

        var order = await GetOrderAsync(orderId);
        var expectedStock = order.Status == OrderStatus.Cancelled ? Stock : Stock - 1;
        Assert.Equal(expectedStock, await GetStockAsync(WarehouseIds.Main, productId));
    }

    private async Task<Guid> CreatePendingApprovalOrderAsync()
    {
        var (creator, _) = await ManagerAsync();
        var (showroomId, productId) = await SeedShowroomAndProductAsync();
        return await CreateAndSubmitAsync(creator, showroomId, productId, 50);
    }

    [Fact]
    public async Task Cancel_RestoresStockToOrderWarehouse()
    {
        var (token, _) = await ManagerAsync();
        var (showroomId, productId) = await SeedShowroomAndProductAsync();
        var orderId = (await CreateOrderAsync(token, showroomId, productId, 0)).GetProperty("id").GetGuid();
        Assert.Equal(Stock - 1, await GetStockAsync(WarehouseIds.Main, productId));

        var response = await OrderFlow.CancelAsync(_client, token, orderId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(Stock, await GetStockAsync(WarehouseIds.Main, productId));
    }

    [Fact]
    public async Task ProductDetails_ShowsTotalWarehouseStock()
    {
        var (token, _) = await ManagerAsync();
        var (_, productId) = await SeedShowroomAndProductAsync();

        var response = await SendAsync(HttpMethod.Get, $"/api/products/{productId}", token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(Stock, json.RootElement.GetProperty("stockQuantity").GetInt32());
    }

    [Fact]
    public async Task OrderDetails_KeepLines_AfterProductIsDeleted()
    {
        var (token, _) = await ManagerAsync();
        var (showroomId, productId) = await SeedShowroomAndProductAsync();
        var created = await CreateOrderAsync(token, showroomId, productId, discount: 5);
        var orderId = created.GetProperty("id").GetGuid();
        var originalName = Line(created).GetProperty("productName").GetString();

        await UpdateProductAsync(productId, deleted: true);

        var details = await GetOrderDetailsAsync(token, orderId);
        var line = Line(details);
        Assert.Equal(originalName, line.GetProperty("productName").GetString());
        Assert.Equal(details.GetProperty("totalAmount").GetDecimal(), LinesTotal(details));
    }

    [Fact]
    public async Task OrderDetails_KeepOriginalName_AfterProductIsRenamed()
    {
        var (token, _) = await ManagerAsync();
        var (showroomId, productId) = await SeedShowroomAndProductAsync();
        var created = await CreateOrderAsync(token, showroomId, productId, discount: 0);
        var orderId = created.GetProperty("id").GetGuid();
        var originalName = Line(created).GetProperty("productName").GetString();

        await UpdateProductAsync(productId, newName: $"Renamed-{Guid.NewGuid():N}");

        var details = await GetOrderDetailsAsync(token, orderId);
        Assert.Equal(originalName, Line(details).GetProperty("productName").GetString());
    }

    private static JsonElement Line(JsonElement order) =>
        Assert.Single(order.GetProperty("lines").EnumerateArray());

    private static decimal LinesTotal(JsonElement order) =>
        order.GetProperty("lines").EnumerateArray().Sum(l =>
            l.GetProperty("quantity").GetInt32() * l.GetProperty("unitPrice").GetDecimal()
            - l.GetProperty("discount").GetDecimal());

    private async Task<JsonElement> GetOrderDetailsAsync(string token, Guid orderId)
    {
        var response = await SendAsync(HttpMethod.Get, $"/api/orders/{orderId}", token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.Clone();
    }

    private async Task UpdateProductAsync(Guid productId, bool deleted = false, string? newName = null)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var product = await db.Products.SingleAsync(p => p.Id == productId);
        if (deleted)
        {
            product.IsDeleted = true;
            product.DeletedAt = DateTime.UtcNow;
        }

        if (newName is not null)
            product.Name = newName;

        await db.SaveChangesAsync();
    }

    private async Task<int> GetStockAsync(Guid warehouseId, Guid productId)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>()
            .WarehouseStocks
            .Where(s => s.WarehouseId == warehouseId && s.ProductId == productId)
            .Select(s => (int?)(s.Quantity - s.Reserved))
            .SingleOrDefaultAsync() ?? 0;
    }

    private async Task<JsonElement> CreateOrderAsync(string token, Guid showroomId, Guid productId, decimal discount)
    {
        var response = await SendAsync(HttpMethod.Post, "/api/orders", token, new
        {
            showroomId,
            warehouseId = WarehouseIds.Main,
            paymentMethod = Enum.GetValues<PaymentMethod>()[0].ToString(),
            shippingAddress = "Test address",
            shippingGovernorate = "Cairo",
            lines = new[] { new { productId, quantity = 1, discount } }
        });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.Clone();
    }

    private async Task<(Guid showroomId, Guid ProductId)> SeedShowroomAndProductAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var suffix = Guid.NewGuid().ToString("N");

        var category = new Category
        {
            Id = Guid.NewGuid(),
            Name = $"Cat-{suffix}"
        };

        var product = new Product
        {
            Id = Guid.NewGuid(),
            CategoryId = category.Id,
            Name = $"Product-{suffix}",
            Sku = $"SKU-{suffix}".ToUpperInvariant(),
            UnitPrice = UnitPrice,
            CostPrice = 50m,
            ReorderLevel = 0
        };


        var showroom = new Showroom
        {
            Id = Guid.NewGuid(),
            Name = $"Showroom-{suffix}",
            Email = $"Showroom-{suffix}@tests.local",
            Address = "Test address",
            City = "Cairo",
            Country = "Egypt",
            PhoneNumber = "01000000000",
            TaxId = $"TAX-{suffix[..12]}"
        };

        var stock = new WarehouseStock
        {
            WarehouseId = WarehouseIds.Main,
            ProductId = product.Id,
            Quantity = Stock
        };
        db.AddRange(category, product, showroom, stock);
        await db.SaveChangesAsync();
        return (showroom.Id, product.Id);
    }

    private async Task<(string Token, Guid Id)> ManagerAsync()
    {
        var user = await factory.CreateUserAsync(RoleIds.Manager);
        return (await LoginAsync(user), user.Id);
    }

    private async Task<Order> GetOrderAsync(Guid id)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>()
            .Orders
            .IgnoreQueryFilters([QueryFilters.Showroom])
            .AsNoTracking()
            .SingleAsync(o => o.Id == id);
    }


    private async Task<string> LoginAsync(TestUser user)
    {
        var response =
            await _client.PostAsJsonAsync("/api/auth/login", new { email = user.Email, password = user.Password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("accessToken").GetString()!;
    }

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
            request.Content = JsonContent.Create(body);

        return _client.SendAsync(request);
    }
    
    private async Task<Guid> CreateAndSubmitAsync(string token, Guid showroomId, Guid productId, decimal discount)
    {
        var orderId = (await CreateOrderAsync(token, showroomId, productId, discount)).GetProperty("id").GetGuid();
        var submit = await OrderFlow.SubmitAsync(_client, token, orderId);
        Assert.True(submit.IsSuccessStatusCode, await submit.Content.ReadAsStringAsync());
        return orderId;
    }
}