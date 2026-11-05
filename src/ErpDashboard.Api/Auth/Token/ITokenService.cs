namespace ErpDashboard.Api.Auth.Token;

public interface ITokenService
{
    AccessToken GenerateToken(User user);
    RefreshToken GenerateRefreshToken();
}