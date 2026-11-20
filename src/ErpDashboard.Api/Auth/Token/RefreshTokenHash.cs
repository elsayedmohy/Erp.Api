namespace ErpDashboard.Api.Auth.Token;

public static class RefreshTokenHash
{
    public static string Compute(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}