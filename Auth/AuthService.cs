namespace ErpDashboard.Api.Auth;

public class AuthService(
    UserManager<User> userManager,
   AppDbContext dbContext,
    ITokenService tokenService
) : IAuthService
{
    public async Task RegisterAsync(RegisterRequestDto request)
    {
        var user = new User
        {
            UserName = request.Email,
            Email = request.Email,
            FirstName = request.FirstName,
            LastName = request.LastName,
            RoleId = RoleIds.User
        };
        var result = await userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            throw new ForbiddenException(result.Errors.First().Description);
        }
    }

    public async Task<LoginDto> LoginAsync(LoginRequestDto request)
    {
        var user = await dbContext.Users.Include(u => u.Role).SingleOrDefaultAsync(u =>  u.Email == request.Email);
        if (user == null)
            throw new UnauthorizedException("Invalid email or password");

        var result = await userManager.CheckPasswordAsync(user, request.Password);
        if (!result)
            throw new UnauthorizedException("Invalid email or password");
        
        
        

        var token = tokenService.GenerateToken(user);
        var refreshToken = await IssueRefreshTokenAsync(user);

        return new LoginDto(token, refreshToken.Token, "Bearer", refreshToken.ExpiresAt);
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

    public async Task LogoutAsync(string token)
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
    
    private async Task<RefreshToken> IssueRefreshTokenAsync(User user)
    {
        var token = tokenService.GenerateRefreshToken();
        PruneRefreshTokens(user);
        user.RefreshTokens.Add(token);
        await userManager.UpdateAsync(user);
        return token;
    }


    private static void PruneRefreshTokens(User user)
    {
        var cutoff = DateTime.UtcNow.AddDays(-7);
        var stale = user.RefreshTokens!
            .Where(t => !t.IsActive && (t.RevokedAt ?? t.ExpiresAt) < cutoff)
            .ToList();

        foreach (var token in stale)
            user.RefreshTokens!.Remove(token);
    }
    
}