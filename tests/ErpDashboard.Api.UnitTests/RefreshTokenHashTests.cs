using ErpDashboard.Api.Auth.Token;

namespace ErpDashboard.Api.UnitTests;

public class RefreshTokenHashTests
{
    [Theory]
    // Published SHA-256 test vectors (FIPS 180-2), as lowercase hex.
    [InlineData("abc", "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad")]
    [InlineData("", "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855")]
    public void Compute_IsLowercaseHexSha256(string token, string expected)
    {
        Assert.Equal(expected, RefreshTokenHash.Compute(token));
    }

    [Fact]
    public void Compute_IsUtf8_NotAscii()
    {
        // "é" is two bytes in UTF-8 (c3 a9). Postgres convert_to(..., 'UTF8') hashes the same bytes.
        Assert.Equal("4a99557e4033c3539de2eb65472017cad5f9557f7a0625a09f1c3f6e2ba69c4c",
            RefreshTokenHash.Compute("é"));
    }

    [Fact]
    public void Compute_IsSixtyFourLowercaseHexCharacters()
    {
        var hash = RefreshTokenHash.Compute(Guid.NewGuid().ToString());

        Assert.Matches("^[0-9a-f]{64}$", hash);
    }
}
