using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ErpDashboard.Api.Data;
using ErpDashboard.Api.Data.Entities;
using ErpDashboard.Api.Data.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ErpDashboard.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class UserShowroomTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task ShowroomUser_WithExistingShowroom_IsCreated()
    {
        var admin = await AdminTokenAsync();
        var showroomId = await SeedShowroomAsync();

        var response = await CreateUserAsync(admin, RoleIds.ShowroomUser, showroomId);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(showroomId, json.RootElement.GetProperty("showroomId").GetGuid());
    }

    [Fact]
    public async Task UnknownShowroom_Returns400()
    {
        var admin = await AdminTokenAsync();

        var response = await CreateUserAsync(admin, RoleIds.ShowroomUser, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ShowroomUser_WithoutShowroom_Returns400()
    {
        var admin = await AdminTokenAsync();

        var response = await CreateUserAsync(admin, RoleIds.ShowroomUser, showroomId: null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task InternalUser_WithShowroom_Returns400()
    {
        var admin = await AdminTokenAsync();
        var showroomId = await SeedShowroomAsync();

        var response = await CreateUserAsync(admin, RoleIds.Manager, showroomId);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdatingInternalUserToShowroom_Returns400()
    {
        var admin = await AdminTokenAsync();
        var showroomId = await SeedShowroomAsync();
        var user = await factory.CreateUserAsync(RoleIds.Manager);

        var response = await SendAsync(HttpMethod.Put, $"/api/users/{user.Id}", admin, new
        {
            firstName = "Test",
            lastName = "User",
            roleId = RoleIds.Manager,
            showroomId
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private Task<HttpResponseMessage> CreateUserAsync(string token, Guid roleId, Guid? showroomId) =>
        SendAsync(HttpMethod.Post, "/api/users", token, new
        {
            firstName = "Showroom",
            lastName = "User",
            email = $"showroom-{Guid.NewGuid():N}@tests.local",
            roleId,
            password = $"Aa1!{Guid.NewGuid():N}",
            showroomId
        });

    private async Task<Guid> SeedShowroomAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var suffix = Guid.NewGuid().ToString("N");

        var showroom = new Showroom
        {
            Id = Guid.NewGuid(),
            Name = $"Showroom-{suffix}",
            Email = $"showroom-{suffix}@tests.local",
            Address = "Address",
            City = "Cairo",
            Country = "Egypt",
            PhoneNumber = "01000000000",
            TaxId = $"TAX-{suffix[..12]}"
        };

        db.Showrooms.Add(showroom);
        await db.SaveChangesAsync();
        return showroom.Id;
    }

    private async Task<string> AdminTokenAsync()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login",
            new { email = ApiFactory.AdminEmail, password = ApiFactory.AdminPassword });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("token").GetString()!;
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