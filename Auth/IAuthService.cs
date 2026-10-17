namespace ErpDashboard.Api.Auth;

public interface IAuthService
{
    Task RegisterAsync(RegisterRequestDto request);
    Task<LoginDto> LoginAsync(LoginRequestDto request);
    Task<LoginDto> RefreshTokenAsync(string token);
    Task LogoutAsync(string token);
}