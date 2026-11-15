using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace ErpDashboard.Api.IntegrationTests;

public class WebhookSigning
{
    public const string Secret = "integration_hmac_secret";
    private static readonly Guid AttemptId = Guid.Parse("11111111-2222-3333-4444-555555555555");
    public static JsonObject Payload(bool success = true, long amount = 102601) => new()
    {
        ["type"] = "TRANSACTION",
        ["obj"] = new JsonObject
        {
            ["id"] = 192036465,
            ["pending"] = false,
            ["amount_cents"] = amount,
            ["success"] = success,
            ["is_auth"] = false,
            ["is_capture"] = false,
            ["is_standalone_payment"] = true,
            ["is_voided"] = false,
            ["is_refunded"] = false,
            ["is_3d_secure"] = true,
            ["integration_id"] = 4242,
            ["has_parent_transaction"] = false,
            ["created_at"] = "2026-11-15T16:00:00.000000",
            ["currency"] = "EGP",
            ["error_occured"] = false,
            ["owner"] = 302852,
            ["order"] = new JsonObject { ["id"] = 217503754, ["merchant_order_id"] = AttemptId.ToString("N") },
            ["source_data"] = new JsonObject { ["pan"] = "2346", ["sub_type"] = "MasterCard", ["type"] = "card" }
        }
    };

    /// Independent re-implementation of the documented recipe, so the test does not just call the code under test.
    public static string Sign(JsonObject payload, string secret = Secret)
    {
        var o = payload["obj"]!;
        string B(string k) => o[k]!.GetValue<bool>() ? "true" : "false";
        var text = $"{o["amount_cents"]}{o["created_at"]}{o["currency"]}{B("error_occured")}{B("has_parent_transaction")}" +
                   $"{o["id"]}{o["integration_id"]}{B("is_3d_secure")}{B("is_auth")}{B("is_capture")}{B("is_refunded")}" +
                   $"{B("is_standalone_payment")}{B("is_voided")}{o["order"]!["id"]}{o["owner"]}{B("pending")}" +
                   $"{o["source_data"]!["pan"]}{o["source_data"]!["sub_type"]}{o["source_data"]!["type"]}{B("success")}";
        return Convert.ToHexStringLower(HMACSHA512.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(text)));
    }
}