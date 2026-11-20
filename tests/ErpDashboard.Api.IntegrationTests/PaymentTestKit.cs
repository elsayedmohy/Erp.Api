using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ErpDashboard.Api.Data;
using ErpDashboard.Api.Data.Entities;
using ErpDashboard.Api.Data.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ErpDashboard.Api.IntegrationTests;

public class PaymentTestKit(ApiFactory factory, HttpClient client)
{
    
    
    public Task<HttpResponseMessage> StartAsync(string token, Guid invoiceId) =>
        SendAsync(HttpMethod.Post, $"/api/invoices/{invoiceId}/online-payments", token);

    public async Task PayAsync(string token, Guid invoiceId, decimal amount)
    {
        var response = await SendAsync(HttpMethod.Post, $"/api/invoices/{invoiceId}/payments", token,
            new { amount, method = Enum.GetValues<PaymentMethod>()[0].ToString() });
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
    }


    public async Task<string> TokenAsync(Guid roleId, Guid? showroomId = null)
    {
        var user = await factory.CreateUserAsync(roleId);
        if (showroomId is { } sid)
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var entity = await db.Users.SingleAsync(u => u.Id == user.Id);
            entity.ShowroomId = sid;
            await db.SaveChangesAsync();
        }

        var response = await client.PostAsJsonAsync("/api/auth/login", new { email = user.Email, password = user.Password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await ReadJsonAsync(response)).GetProperty("accessToken").GetString()!;
    }

    public  async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.Clone();
    }

    public Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
            request.Content = JsonContent.Create(body);
        return client.SendAsync(request);
    }
    
    public  async Task<(Guid ShowroomId, Guid InvoiceId)> IssuedInvoiceAsync(string accountant, decimal unitPrice)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var showroom = new Showroom
        {
            Id = Guid.NewGuid(), Name = $"Showroom-{suffix}", Email = $"showroom-{suffix}@tests.local",
            Address = "Test address", City = "Cairo", Country = "Egypt",
            PhoneNumber = "01000000000", TaxId = $"TAX-{suffix[..12]}"
        };
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Showrooms.Add(showroom);
            await db.SaveChangesAsync();
        }

        var created = await SendAsync(HttpMethod.Post, "/api/invoices", accountant, new
        {
            showroomId = showroom.Id,
            lines = new[] { new { description = "Sofa", quantity = 1, unitPrice } }
        });
        Assert.True(created.StatusCode == HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var id = (await ReadJsonAsync(created)).GetProperty("id").GetGuid();

        var issued = await SendAsync(HttpMethod.Patch, $"/api/invoices/{id}/issue", accountant);
        Assert.True(issued.IsSuccessStatusCode, await issued.Content.ReadAsStringAsync());
        return (showroom.Id, id);
    }

}