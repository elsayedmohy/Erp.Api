namespace ErpDashboard.Api.Auth;

public record LoginRequestDto(string Email, string Password);

public record 
    RegisterRequestDto(
    string FirstName,
    string LastName,
    string Email,
    string Password);

public record TokensResponseDto(string accessToken,string RefreshToken);
public record RefreshTokenRequestDto(string RefreshToken);

public record LoginDto(
    string Token,
    string RefreshToken,
    string TokenType,
    DateTime ExpiresAt);