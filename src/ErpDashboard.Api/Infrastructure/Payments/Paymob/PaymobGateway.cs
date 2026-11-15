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

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

        using var message =
            new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(_options.ApiBaseUrl), "/v1/intention/"))
            {
                Content = JsonContent.Create(body, options: Json)
            };
        message.Headers.TryAddWithoutValidation("Authorization", $"Token {_options.SecretKey}");

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

        using (response)
        {
            if ((int)response.StatusCode >= 500)
                throw new PaymentProviderUnavailableException(
                    $"The payment provider returned {(int)response.StatusCode}.");

            if (!response.IsSuccessStatusCode)
            {

                var error = await response.Content.ReadAsStringAsync(CancellationToken.None);
                logger.LogError("Payment provider rejected the checkout request with {Status}: {Body}",
                    (int)response.StatusCode, error.Length > 500 ? error[..500] : error);
                throw new InvalidOperationException(
                    $"The payment provider rejected the request ({(int)response.StatusCode}).");
            }

            var intention = await response.Content.ReadFromJsonAsync<IntentionResponse>(Json, timeout.Token)
                            ?? throw new InvalidOperationException("The payment provider returned an empty response.");

            var url = $"{_options.CheckoutBaseUrl.TrimEnd('/')}/?publicKey={Uri.EscapeDataString(_options.PublicKey)}" +
                      $"&clientSecret={Uri.EscapeDataString(intention.ClientSecret)}";

            return new CheckoutSession(intention.Id, new Uri(url));
        }
    }


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
}