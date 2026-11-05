namespace ErpDashboard.Api.Auth;

public class AuthService(
    UserManager<User> userManager,
    SignInManager<User> signInManager,
    AppDbContext dbContext,
    ITokenService tokenService
) : IAuthService
{
    private const string InvalidCredentials = "Invalid email or password.";
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
            throw result.ToValidationException();
        }
    }

    public async Task<LoginDto> LoginAsync(LoginRequestDto request)
    {
        var normalizedEmail = userManager.NormalizeEmail(request.Email);
        var user = await dbContext.Users.Include(u => u.Role)
            .SingleOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail);
        if (user == null)
            throw new UnauthorizedException(InvalidCredentials);
        
        var result = await signInManager.CheckPasswordSignInAsync(
            user,
            request.Password,
            lockoutOnFailure: true
        );
         if (result.IsLockedOut)
        {
            throw new AccountLockedException();
        }
        if (!result.Succeeded)
        {
            throw new  UnauthorizedException(InvalidCredentials);
        }
        await userManager.UpdateSecurityStampAsync(user);
        AccessToken accessToken = tokenService.GenerateToken(user);
        var refreshToken = await IssueRefreshTokenAsync(user);

        return new LoginDto(accessToken.Token, refreshToken.Token, "Bearer", accessToken.ExpiresAt);
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
        AccessToken accessToken = tokenService.GenerateToken(user);
        return new LoginDto(accessToken.Token, next.Token, "Bearer", accessToken.ExpiresAt);
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