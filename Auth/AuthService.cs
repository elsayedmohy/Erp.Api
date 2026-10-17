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

    public async Task<LoginDto> RefreshTokenAsync(string token)
    {
        var current = await dbContext.Set<RefreshToken>()
                          .SingleOrDefaultAsync(t => t.Token == token)
                      ?? throw new UnauthorizedException("Invalid refresh token.");

        if (current.RevokedAt is not null)
        {
            await dbContext.Set<RefreshToken>()
                .Where(t => t.FamilyId == current.FamilyId && t.RevokedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, DateTime.UtcNow));
            throw new UnauthorizedException("Invalid refresh token.");
        }

        if (current.IsExpired)
            throw new UnauthorizedException("Refresh token expired.");

        var user = await dbContext.Users
            .Include(u => u.Role)              
            .SingleAsync(u => u.Id == current.UserId);

        current.RevokedAt = DateTime.UtcNow;

        var next = tokenService.GenerateRefreshToken();
        next.UserId = user.Id;
        next.FamilyId = current.FamilyId;             
        dbContext.Add(next);

        await dbContext.SaveChangesAsync();

        return new LoginDto(tokenService.GenerateToken(user), next.Token, "Bearer", next.ExpiresAt);
    }

    public async Task LogoutAsync(string token)
    {
        var current = await dbContext.Set<RefreshToken>()
            .SingleOrDefaultAsync(t => t.Token == token);

        if (current is null || current.RevokedAt is not null) return;

        current.RevokedAt = DateTime.UtcNow;
        await dbContext.SaveChangesAsync();
    }
    
    private async Task<RefreshToken> IssueRefreshTokenAsync(User user)
    {
        var token = tokenService.GenerateRefreshToken();
        token.UserId = user.Id;
        token.FamilyId = Guid.NewGuid();        
        dbContext.Add(token);
        await dbContext.SaveChangesAsync();
        return token;
    }

    
}