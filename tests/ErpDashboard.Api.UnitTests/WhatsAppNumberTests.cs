using ErpDashboard.Api.Features.Notifications;

namespace ErpDashboard.Api.UnitTests;

public class WhatsAppNumberTests
{
    [Theory]
    [InlineData("01012345678", "201012345678")]
    [InlineData("010 1234 5678", "201012345678")]
    [InlineData("+20 10 1234 5678", "201012345678")]
    [InlineData("00201512345678", "201512345678")]
    [InlineData("201112345678", "201112345678")]
    [InlineData("012-1234-5678", "201212345678")]
    public void EgyptianMobiles_AreNormalized(string raw, string expected) =>
        Assert.Equal(expected, WhatsAppNumber.TryNormalize(raw));

    [Theory]
    [InlineData("0225551234")]        // Cairo landline
    [InlineData("0572345678")]        // Damietta landline
    [InlineData("01312345678")]       // no such mobile prefix
    [InlineData("0101234567")]        // one digit short
    [InlineData("")]
    [InlineData(null)]
    public void EverythingElse_IsNull(string? raw) => Assert.Null(WhatsAppNumber.TryNormalize(raw));
}