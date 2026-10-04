


namespace ErpDashboard.Api.Auth;

public class AuthService(
    UserManager<User> userManager,
    ITokenService  tokenService
    ):IAuthService
{
    public async Task RegisterAsync(RegisterRequestDto request)
    {
        var user = new User
        {
            UserName = request.Email,
            Email = request.Email,
            FirstName = request.FirstName,
            LastName = request.LastName,
        };
        var result = await userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            throw new ForbiddenException(result.Errors.First().Description);
        }
        
    }

    public async Task<LoginDto> LoginAsync(LoginRequestDto request)
    {
        var user = await userManager.FindByEmailAsync(request.Email);
        if (user == null)
            throw new UnauthorizedException("Invalid email or password");
        
        var result = await userManager.CheckPasswordAsync(user,request.Password);
        if (!result)
            throw new ForbiddenException();

        var token =  tokenService.GenerateToken(user);
        var refreshToken =  tokenService.GenerateRefreshToken();
        
        return new LoginDto(token,refreshToken.Token, "Bearer",refreshToken.ExpiresAt );
    }

    public async Task<TokensResponseDto> RefreshTokenAsync(string token)
    {
        var user = await userManager.Users.SingleOrDefaultAsync(user =>
            user.RefreshTokens.Any(refreshToken => refreshToken.Token == token));
        if (user is null)
            throw new UnauthorizedException();
        
        var refreshToken = user.RefreshTokens.SingleOrDefault(refreshToken => refreshToken.Token == token);
        if (!refreshToken.IsActive)
        {
            throw new UnauthorizedException();
        }
        
        refreshToken.RevokedAt = DateTime.UtcNow;
        var newRefreshToken = tokenService.GenerateRefreshToken();
        var accessToken = tokenService.GenerateToken(user);
        user.RefreshTokens.Add(newRefreshToken);
        await userManager.UpdateAsync(user);

        return new TokensResponseDto(
            accessToken: accessToken,
            RefreshToken: newRefreshToken.Token
        );
    }

    public async Task LogoutAsync(string  token)
    {
        var user = await userManager.Users.SingleOrDefaultAsync(user =>
            user.RefreshTokens.Any(refreshToken => refreshToken.Token == token));
        if (user is null)
            throw new UnauthorizedException();
        
        var refreshToken = user.RefreshTokens.SingleOrDefault(refreshToken => refreshToken.Token == token);
        if (!refreshToken.IsActive)
        {
            throw new UnauthorizedException();
        }
        
        refreshToken.RevokedAt = DateTime.UtcNow;
        await userManager.UpdateAsync(user);
    }
}