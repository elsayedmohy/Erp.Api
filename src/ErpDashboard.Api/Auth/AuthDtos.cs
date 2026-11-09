namespace ErpDashboard.Api.Auth;

public record LoginRequestDto(string Email, string Password);


public record AccessToken(string Token , DateTime ExpiresAt);
public record RefreshTokenRequestDto(string RefreshToken);

public record LoginDto(
    string Token,
    string RefreshToken,
    string TokenType,
    DateTime ExpiresAt);