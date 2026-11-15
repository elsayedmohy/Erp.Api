using ErpDashboard.Api.Infrastructure.Payments.PaymentWebhooks;

namespace ErpDashboard.Api.Infrastructure.Payments.Paymob;

public sealed class PaymobWebhookVerifier(IOptions<PaymobOptions> options) : IPaymentWebhookVerifier
{
    private readonly byte[] _key = Encoding.UTF8.GetBytes(options.Value.HmacSecret);

    public string Provider => "paymob";

    public WebhookVerification Verify(string body, IQueryCollection query, IHeaderDictionary headers)
    {
        var received = query["hmac"].ToString();
        if (string.IsNullOrWhiteSpace(received))
            return WebhookVerification.Invalid("Missing hmac.");

        JsonElement obj;
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("obj", out var o) || o.ValueKind != JsonValueKind.Object)
                return WebhookVerification.Invalid("Missing obj.");
            obj = o.Clone();
        }
        catch (JsonException)
        {
            return WebhookVerification.Invalid("Body is not valid JSON.");
        }

        string concatenated;
        try
        {
            concatenated = Concatenate(obj);
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException)
        {
            return WebhookVerification.Invalid("Payload is missing signed fields.");
        }

        var expected = HMACSHA512.HashData(_key, Encoding.UTF8.GetBytes(concatenated));
        byte[] actual;
        try { actual = Convert.FromHexString(received); }
        catch (FormatException) { return WebhookVerification.Invalid("hmac is not hex."); }


        if (!CryptographicOperations.FixedTimeEquals(expected, actual))
            return WebhookVerification.Invalid("Signature mismatch.");

        var order = obj.GetProperty("order");
        var reference = order.TryGetProperty("merchant_order_id", out var m) ? m.ToString() : null;

        return WebhookVerification.Valid(new PaymentNotification(
            ProviderEventId: obj.GetProperty("id").ToString(),
            AttemptId: Guid.TryParseExact(reference, "N", out var attemptId) ? attemptId : null,
            Success: obj.GetProperty("success").GetBoolean(),
            Pending: obj.GetProperty("pending").GetBoolean(),
            IsRefundOrVoid: obj.GetProperty("is_refunded").GetBoolean() || obj.GetProperty("is_voided").GetBoolean(),
            AmountMinor: obj.GetProperty("amount_cents").GetInt64(),
            Currency: obj.GetProperty("currency").GetString() ?? ""));
    }

    // The 20 signed values, in the exact order Paymob documents.
    public static string Concatenate(JsonElement obj)
    {
        var source = obj.GetProperty("source_data");
        var order = obj.GetProperty("order");

        return string.Concat(
            Value(obj, "amount_cents"),
            Value(obj, "created_at"),
            Value(obj, "currency"),
            Value(obj, "error_occured"),
            Value(obj, "has_parent_transaction"),
            Value(obj, "id"),
            Value(obj, "integration_id"),
            Value(obj, "is_3d_secure"),
            Value(obj, "is_auth"),
            Value(obj, "is_capture"),
            Value(obj, "is_refunded"),
            Value(obj, "is_standalone_payment"),
            Value(obj, "is_voided"),
            Value(order, "id"),
            Value(obj, "owner"),
            Value(obj, "pending"),
            Value(source, "pan"),
            Value(source, "sub_type"),
            Value(source, "type"),
            Value(obj, "success"));
    }

    private static string Value(JsonElement parent, string name)
    {
        var e = parent.GetProperty(name);
        return e.ValueKind switch
        {
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.String => e.GetString()!,
            JsonValueKind.Number => e.GetRawText(),
            JsonValueKind.Null => "",
            _ => throw new InvalidOperationException($"Unexpected type for {name}.")
        };
    }
}