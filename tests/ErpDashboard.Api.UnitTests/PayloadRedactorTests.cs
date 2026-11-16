using System.Text.Json;
using ErpDashboard.Api.Features.Payments.Webhooks;

namespace ErpDashboard.Api.UnitTests;

public class PayloadRedactorTests
{
    private const string Payload = """
    {
      "obj": {
        "id": 574891455,
        "amount_cents": 11400,
        "source_data": { "pan": "2346", "type": "card" },
        "order": {
          "shipping_data": { "first_name": "Sandbox", "email": "a@b.c", "phone_number": "0100", "name": null },
          "merchant": { "phones": ["+2010"] }
        },
        "payment_key_claims": { "redirect_url": "https://x/?payment_token=SECRET" }
      }
    }
    """;

    private static JsonElement Obj(string redacted) => JsonDocument.Parse(redacted).RootElement.GetProperty("obj");

    [Fact]
    public void PersonalData_AndTokens_AreMasked_AtAnyDepth()
    {
        var redacted = PayloadRedactor.Redact(Payload);

        Assert.DoesNotContain("a@b.c", redacted);
        Assert.DoesNotContain("0100", redacted);
        Assert.DoesNotContain("+2010", redacted);
        Assert.DoesNotContain("SECRET", redacted);
        var shipping = Obj(redacted).GetProperty("order").GetProperty("shipping_data");
        Assert.Equal(PayloadRedactor.Mask, shipping.GetProperty("email").GetString());
    }

    [Fact]
    public void FieldsTheAdminNeeds_AreKept()
    {
        var obj = Obj(PayloadRedactor.Redact(Payload));

        Assert.Equal(574891455, obj.GetProperty("id").GetInt64());
        Assert.Equal(11400, obj.GetProperty("amount_cents").GetInt64());
        Assert.Equal("2346", obj.GetProperty("source_data").GetProperty("pan").GetString());
    }

    [Fact]
    public void NullStaysNull_SoTheAdminCanTellEmptyFromHidden()
    {
        var shipping = Obj(PayloadRedactor.Redact(Payload)).GetProperty("order").GetProperty("shipping_data");

        Assert.Equal(JsonValueKind.Null, shipping.GetProperty("name").ValueKind);
    }

    [Fact]
    public void NonJson_IsReturnedTruncated_WithoutThrowing()
    {
        var junk = new string('x', 10_000);

        var result = PayloadRedactor.Redact(junk);

        Assert.True(result.Length <= 2049);
    }
}