using System.Text.Json;
using System.Text.Json.Nodes;

namespace ErpDashboard.Api.Features.Payments.Webhooks;

// Hides personal data and bearer-like values before a stored payload is shown to a person.
// Key-based, so it works for any provider's JSON.
public static class PayloadRedactor
{
    public const string Mask = "[redacted]";
    private const int MaxNonJsonLength = 2048;

    private static readonly HashSet<string> SensitiveKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "email", "phone_number", "phones", "first_name", "last_name",
        "street", "building", "floor", "apartment", "postal_code",
        "address_line_1", "address_line_2",
        "redirect_url", "payment_token", "token", "client_secret"
    };

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    public static string Redact(string payload)
    {
        JsonNode? root;
        try { root = JsonNode.Parse(payload); }
        catch (JsonException)
        {
            
            return payload.Length > MaxNonJsonLength ? payload[..MaxNonJsonLength] + "…" : payload;
        }

        if (root is null) return payload;
        Walk(root);
        return root.ToJsonString(Indented);
    }

    private static void Walk(JsonNode node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(p => p.Key).ToList())
                {
                    if (SensitiveKeys.Contains(key))
                    {
                        if (obj[key] is not null) obj[key] = Mask;
                    }
                    else if (obj[key] is { } child)
                    {
                        Walk(child);
                    }
                }
                break;

            case JsonArray array:
                foreach (var child in array)
                    if (child is not null) Walk(child);
                break;
        }
    }
}