namespace ErpDashboard.Api.Auth.Token;

public interface ITokenService
{
    AccessToken GenerateToken(User user);
    IssuedRefreshToken GenerateRefreshToken();
}