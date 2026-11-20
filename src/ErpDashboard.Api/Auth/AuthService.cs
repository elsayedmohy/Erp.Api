using ILogger = Serilog.ILogger;

namespace ErpDashboard.Api.Auth;

public class AuthService(
    UserManager<User> userManager,
    SignInManager<User> signInManager,
    AppDbContext dbContext,
    ITokenService tokenService,
    ILogger<AuthService> logger
) : IAuthService
{
    private const string InvalidCredentials = "Invalid email or password.";
    private const string InvalidRefreshToken = "Invalid refresh token.";
    private static readonly TimeSpan ReuseGracePeriod = TimeSpan.FromSeconds(10);


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
        AccessToken accessToken = tokenService.GenerateToken(user);
        var refreshToken = await IssueRefreshTokenAsync(user);

        return new LoginDto(accessToken.Token, refreshToken, accessToken.ExpiresAt);
    }
    
public async Task<LoginDto> RefreshTokenAsync(string token)
{
    var tokenHash = RefreshTokenHash.Compute(token);
    var current = await dbContext.Set<RefreshToken>()
                      .AsNoTracking()
                      .SingleOrDefaultAsync(t => t.TokenHash == tokenHash)
                  ?? throw new UnauthorizedException(InvalidRefreshToken);

    if (current.RotatedAt is { } rotatedAt)
    {
        if (DateTime.UtcNow - rotatedAt > ReuseGracePeriod)
        {
            logger.LogWarning("Refresh token reuse detected for user {UserId}", current.UserId);

            var owner = await userManager.FindByIdAsync(current.UserId.ToString());
            if (owner is not null)
                await RevokeAllSessionsAsync(owner);
        }

        throw new UnauthorizedException(InvalidRefreshToken);
    }

    if (current.RevokedAt is not null)
        throw new UnauthorizedException(InvalidRefreshToken);

    if (current.IsExpired)
        throw new UnauthorizedException("Refresh token expired.");

    var user = await dbContext.Users
        .Include(u => u.Role)
        .SingleAsync(u => u.Id == current.UserId);

    await using var transaction = await dbContext.Database.BeginTransactionAsync();

    DateTime? now = DateTime.UtcNow;
    var claimed = await dbContext.Set<RefreshToken>()
        .Where(t => t.Id == current.Id && t.RevokedAt == null)
        .ExecuteUpdateAsync(s => s
            .SetProperty(t => t.RevokedAt, now)
            .SetProperty(t => t.RotatedAt, now));

    if (claimed == 0)
        throw new UnauthorizedException(InvalidRefreshToken);

    var next = tokenService.GenerateRefreshToken();
    next.Entity.UserId = user.Id;
    next.Entity.FamilyId = current.FamilyId;
    dbContext.Add(next.Entity);

    await dbContext.SaveChangesAsync();
    await transaction.CommitAsync();

    AccessToken accessToken = tokenService.GenerateToken(user);
    return new LoginDto(accessToken.Token, next.Value,accessToken.ExpiresAt);}

public async Task RevokeAllSessionsAsync(ClaimsPrincipal principal)
{
    var user = await userManager.GetUserAsync(principal)
               ?? throw new UnauthorizedException(InvalidCredentials);

    await RevokeAllSessionsAsync(user);
}

public async Task RevokeAllSessionsAsync(Guid userId)
{
    var user = await userManager.FindByIdAsync(userId.ToString())
               ?? throw new NotFoundException("User", userId);

    await RevokeAllSessionsAsync(user);
}

private async Task RevokeAllSessionsAsync(User user)
{
    await using var transaction = await dbContext.Database.BeginTransactionAsync();

    var result = await userManager.UpdateSecurityStampAsync(user);
    if (!result.Succeeded)
        throw result.ToValidationException();

    await dbContext.Set<RefreshToken>()
        .Where(t => t.UserId == user.Id && t.RevokedAt == null)
        .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, DateTime.UtcNow));

    await transaction.CommitAsync();
}

    public async Task LogoutAsync(string token)
    {
        var tokenHash = RefreshTokenHash.Compute(token);
        var current = await dbContext.Set<RefreshToken>()
            .SingleOrDefaultAsync(t => t.TokenHash == tokenHash);

        if (current is null || current.RevokedAt is not null) return;

        current.RevokedAt = DateTime.UtcNow;
        await dbContext.SaveChangesAsync();
    }
    

    private async Task<string> IssueRefreshTokenAsync(User user)
    {
        var issued = tokenService.GenerateRefreshToken();
        issued.Entity.UserId = user.Id;
        issued.Entity.FamilyId = Guid.NewGuid();
        dbContext.Add(issued.Entity);
        await dbContext.SaveChangesAsync();
        return issued.Value;
    }
}