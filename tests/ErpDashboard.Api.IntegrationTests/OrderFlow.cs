using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace ErpDashboard.Api.IntegrationTests;

public static class OrderFlow
{
    public static Task<HttpResponseMessage> SubmitAsync(HttpClient client, string token, Guid orderId) =>
        SendAsync(client, HttpMethod.Post, $"/api/orders/{orderId}/submit", token);

    public static Task<HttpResponseMessage> SetStatusAsync(HttpClient client, string token, Guid orderId, string status) =>
        SendAsync(client, HttpMethod.Patch, $"/api/orders/{orderId}/status", token, new { status });

    public static Task<HttpResponseMessage> CancelAsync(HttpClient client, string token, Guid orderId, string reason = "Customer changed mind") =>
        SendAsync(client, HttpMethod.Post, $"/api/orders/{orderId}/cancel", token, new { reason });

    public static async Task AdvanceAsync(HttpClient client, string token, Guid orderId, string target)
    {
        await ExpectOkAsync(SubmitAsync(client, token, orderId));
        foreach (var step in new[] { "Preparing", "Shipped", "Delivered" })
        {
            if (target == "Confirmed") return;
            await ExpectOkAsync(SetStatusAsync(client, token, orderId, step));
            if (step == target) return;
        }
    }

    private static async Task ExpectOkAsync(Task<HttpResponseMessage> call)
    {
        var response = await call;
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string url, string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
            request.Content = JsonContent.Create(body);
        return client.SendAsync(request);
    }
}