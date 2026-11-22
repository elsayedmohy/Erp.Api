namespace ErpDashboard.Api.Infrastructure.Payments.Paymob;

public sealed class PaymobGateway(HttpClient http, IOptions<PaymobOptions> options, ILogger<PaymobGateway> logger)
    : IPaymentGateway
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly PaymobOptions _options = options.Value;

    public string Name => "paymob";

    public async Task<CheckoutSession> CreateCheckoutAsync(CheckoutRequest request, CancellationToken ct)
    {
        var body = new IntentionRequest(
            Amount: request.AmountMinor,
            Currency: request.Currency,
            PaymentMethods: [_options.IntegrationId],
            Items: [new IntentionItem(request.Description, request.AmountMinor, 1)],
            BillingData: new BillingData(request.Payer.Name, "NA", request.Payer.Email, request.Payer.Phone, "EG"),
            SpecialReference: request.AttemptId.ToString("N"),
            NotificationUrl: _options.NotificationUrl,
            RedirectionUrl: _options.RedirectionUrl);

        using var response = await SendAsync("/v1/intention/", body, ct);
        if (!response.IsSuccessStatusCode)
        {
            await LogRejectionAsync("checkout", response);
            throw new InvalidOperationException(
                $"The payment provider rejected the request ({(int)response.StatusCode}).");
        }

        var intention = await response.Content.ReadFromJsonAsync<IntentionResponse>(Json, ct)
                        ?? throw new InvalidOperationException("The payment provider returned an empty response.");

        var url = $"{_options.CheckoutBaseUrl.TrimEnd('/')}/?publicKey={Uri.EscapeDataString(_options.PublicKey)}" +
                  $"&clientSecret={Uri.EscapeDataString(intention.ClientSecret)}";
        return new CheckoutSession(intention.Id, new Uri(url));
    }

    public Task<ReversalResult> VoidAsync(string providerTransactionId, CancellationToken ct) =>
        ReverseAsync("/api/acceptance/void_refund/void",
            new VoidRequest(providerTransactionId), "void", ct);

    public Task<ReversalResult> RefundAsync(string providerTransactionId, long amountMinor, CancellationToken ct) =>
        ReverseAsync("/api/acceptance/void_refund/refund",
            new RefundRequest(providerTransactionId, amountMinor), "refund", ct);

    private async Task<ReversalResult> ReverseAsync(string path, object body, string kind, CancellationToken ct)
    {
        using var response = await SendAsync(path, body, ct);
        if (!response.IsSuccessStatusCode)
        {
            await LogRejectionAsync(kind, response);
            return ReversalResult.Rejected($"The provider rejected the {kind} ({(int)response.StatusCode}).");
        }

        // Paymob answers with the new transaction; "success" says whether the bank accepted it.
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var root = doc.RootElement;
        var success = root.TryGetProperty("success", out var s) && s.ValueKind == JsonValueKind.True;
        var reference = root.TryGetProperty("id", out var id) ? id.ToString() : null;
        return success
            ? ReversalResult.Ok(reference)
            : ReversalResult.Rejected($"The provider did not accept the {kind}.");
    }

    private Task<HttpResponseMessage> SendAsync(string path, object body, CancellationToken ct) =>
        SendAsync(HttpMethod.Post, path, body, $"Token {_options.SecretKey}", ct);

    /// Timeouts and 5xx become PaymentProviderUnavailableException; everything else is returned to the caller.
    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method, string path, object? body, string? authorization, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

        using var message = new HttpRequestMessage(method, new Uri(new Uri(_options.ApiBaseUrl), path));
        if (body is not null)
            message.Content = JsonContent.Create(body, options: Json);
        if (authorization is not null)
            message.Headers.TryAddWithoutValidation("Authorization", authorization);
        
        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(message, timeout.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new PaymentProviderUnavailableException(
                $"The payment provider did not answer within {_options.TimeoutSeconds} seconds.");
        }
        catch (HttpRequestException ex)
        {
            throw new PaymentProviderUnavailableException("The payment provider could not be reached.", ex);
        }

        if ((int)response.StatusCode >= 500)
        {
            response.Dispose();
            throw new PaymentProviderUnavailableException($"The payment provider returned {(int)response.StatusCode}.");
        }

        return response;
    }

    private async Task LogRejectionAsync(string kind, HttpResponseMessage response)
    {
        var error = await response.Content.ReadAsStringAsync(CancellationToken.None);
        logger.LogError("Payment provider rejected the {Kind} request with {Status}: {Body}",
            kind, (int)response.StatusCode, error.Length > 500 ? error[..500] : error);
    }

    public async Task<TransactionState> GetTransactionAsync(string providerTransactionId, CancellationToken ct)
    {
        var authToken = await GetAuthTokenAsync(ct);

        using var response = await SendAsync(HttpMethod.Get,
            $"/api/acceptance/transactions/{Uri.EscapeDataString(providerTransactionId)}",
            null, $"Bearer {authToken}", ct);

        // Any failure here must stop the retry, never let it through: unknown state means "do not reverse".
        if (!response.IsSuccessStatusCode)
        {
            await LogRejectionAsync("inquiry", response);
            throw new PaymentProviderUnavailableException(
                $"Transaction inquiry failed ({(int)response.StatusCode}).");
        }

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var root = doc.RootElement;
        var voided = root.TryGetProperty("is_voided", out var v) && v.ValueKind == JsonValueKind.True;
        var refunded = root.TryGetProperty("refunded_amount_cents", out var r) && r.ValueKind == JsonValueKind.Number
            ? (long)r.GetDecimal()
            : 0;

        return new TransactionState(voided, refunded);
    }

    private async Task<string> GetAuthTokenAsync(CancellationToken ct)
    {
        using var response = await SendAsync(HttpMethod.Post, "/api/auth/tokens",
            new AuthTokenRequest(_options.ApiKey), null, ct);

        if (!response.IsSuccessStatusCode)
        {
            await LogRejectionAsync("auth", response);
            throw new PaymentProviderUnavailableException(
                $"The payment provider refused the API key ({(int)response.StatusCode}).");
        }

        var body = await response.Content.ReadFromJsonAsync<AuthTokenResponse>(Json, ct);
        return body?.Token ?? throw new PaymentProviderUnavailableException("The payment provider returned no auth token.");
    }

    internal sealed record VoidRequest(string TransactionId);

    internal sealed record RefundRequest(string TransactionId, long AmountCents);


    internal sealed record IntentionRequest(
        long Amount,
        string Currency,
        int[] PaymentMethods,
        IntentionItem[] Items,
        BillingData BillingData,
        string SpecialReference,
        string? NotificationUrl,
        string? RedirectionUrl);

    internal sealed record IntentionItem(string Name, long Amount, int Quantity);

    internal sealed record BillingData(
        string FirstName,
        string LastName,
        string Email,
        string PhoneNumber,
        string Country);

    internal sealed record IntentionResponse(string Id, string ClientSecret);
    internal sealed record AuthTokenRequest(string ApiKey);
    internal sealed record AuthTokenResponse(string Token);
}