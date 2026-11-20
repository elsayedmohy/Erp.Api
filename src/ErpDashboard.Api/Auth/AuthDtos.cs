namespace ErpDashboard.Api.Auth;

public record LoginRequestDto(string Email, string Password);


public record AccessToken(string Token , DateTime ExpiresAt);
public record RefreshTokenRequestDto(string RefreshToken);

public record LoginDto(
    string AccessToken,
    string RefreshToken,
    DateTime ExpiresAt);
    
public sealed record IssuedRefreshToken(string Value, RefreshToken Entity);