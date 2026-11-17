namespace ErpDashboard.Api.Infrastructure.Messaging.OpenWa;

public sealed class OpenWaSender(HttpClient http, IOptions<OpenWaOptions> options, ILogger<OpenWaSender> logger)
    : IWhatsAppSender
{
    private readonly OpenWaOptions _options = options.Value;

    private static readonly HashSet<HttpStatusCode> Transient =
        [HttpStatusCode.RequestTimeout, HttpStatusCode.Conflict, HttpStatusCode.Locked, HttpStatusCode.TooManyRequests];

    public async Task<WhatsAppSendResult> SendTextAsync(string recipient, string text, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

        var path = $"/api/sessions/{Uri.EscapeDataString(_options.SessionId)}/messages/send-text";
        using var message = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(_options.BaseUrl), path))
        {
            Content = JsonContent.Create(new { chatId = $"{recipient}@c.us", text })
        };
        message.Headers.TryAddWithoutValidation("X-API-Key", _options.ApiKey);

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(message, timeout.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new WhatsAppUnavailableException($"WhatsApp gateway did not answer within {_options.TimeoutSeconds} seconds.");
        }
        catch (HttpRequestException ex)
        {
            throw new WhatsAppUnavailableException("WhatsApp gateway could not be reached.", ex);
        }

        using (response)
        {
            if ((int)response.StatusCode >= 500 || Transient.Contains(response.StatusCode))
                throw new WhatsAppUnavailableException($"WhatsApp gateway returned {(int)response.StatusCode}.");

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("WhatsApp gateway rejected a message with {Status}.", (int)response.StatusCode);
                return WhatsAppSendResult.Rejected($"WhatsApp gateway rejected the message ({(int)response.StatusCode}).");
            }

            string? id = null;
            try
            {
                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
                if (doc.RootElement.TryGetProperty("id", out var idElement)) id = idElement.ToString();
            }
            catch (JsonException ex) 
            {
                logger.LogWarning("WhatsApp exception", ex);
            }

            return WhatsAppSendResult.Sent(id);
        }
    }
}