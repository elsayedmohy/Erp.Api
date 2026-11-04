namespace ErpDashboard.Api.Auth.Token;

public interface ITokenService
{
    string GenerateToken(User user);
    RefreshToken GenerateRefreshToken();
}