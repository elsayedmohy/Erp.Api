namespace ErpDashboard.Api.Auth;

public interface IAuthService
{
    Task<LoginDto> LoginAsync(LoginRequestDto request);
    Task<LoginDto> RefreshTokenAsync(string token);
    Task LogoutAsync(string token);
    Task RevokeAllSessionsAsync(ClaimsPrincipal user);
    Task RevokeAllSessionsAsync(Guid userId);
}