using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace ErpDashboard.Api.IntegrationTests;

public static class OrderFlow
{
    public static async Task<uint> VersionAsync(HttpClient client, string token, Guid orderId)
    {
        var response = await SendAsync(client, HttpMethod.Get, $"/api/orders/{orderId}", token, version: null);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("version").GetUInt32();
    }

    public static async Task<HttpResponseMessage> SubmitAsync(HttpClient client, string token, Guid orderId) =>
        await SendAsync(client, HttpMethod.Post, $"/api/orders/{orderId}/submit", token,
            await VersionAsync(client, token, orderId));

    public static async Task<HttpResponseMessage> SetStatusAsync(HttpClient client, string token, Guid orderId, string status) =>
        await SendAsync(client, HttpMethod.Patch, $"/api/orders/{orderId}/status", token,
            await VersionAsync(client, token, orderId), new { status });

    public static async Task<HttpResponseMessage> CancelAsync(HttpClient client, string token, Guid orderId, string reason = "Customer changed mind") =>
        await SendAsync(client, HttpMethod.Post, $"/api/orders/{orderId}/cancel", token,
            await VersionAsync(client, token, orderId), new { reason });

    public static async Task<HttpResponseMessage> ApproveAsync(HttpClient client, string token, Guid orderId) =>
        await SendAsync(client, HttpMethod.Post, $"/api/orders/{orderId}/approve", token,
            await VersionAsync(client, token, orderId));

    public static async Task<HttpResponseMessage> RejectAsync(HttpClient client, string token, Guid orderId, string reason) =>
        await SendAsync(client, HttpMethod.Post, $"/api/orders/{orderId}/reject", token,
            await VersionAsync(client, token, orderId), new { reason });

    public static async Task<HttpResponseMessage> UpdateAsync(HttpClient client, string token, Guid orderId, object body) =>
        await SendAsync(client, HttpMethod.Put, $"/api/orders/{orderId}", token,
            await VersionAsync(client, token, orderId), body);
    
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

    public static Task<HttpResponseMessage> SendAsync(
        HttpClient client, HttpMethod method, string url, string token, uint? version, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (version is { } v)
            request.Headers.IfMatch.Add(new EntityTagHeaderValue($"\"{v}\""));
        if (body is not null)
            request.Content = JsonContent.Create(body);
        return client.SendAsync(request);
    }

    private static async Task ExpectOkAsync(Task<HttpResponseMessage> call)
    {
        var response = await call;
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }
}