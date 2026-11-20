
namespace ErpDashboard.Api.Auth.Token;

public class TokenService(IOptions<JwtSettings> settings) : ITokenService
{
    private readonly JwtSettings _settings = settings.Value;

    public AccessToken GenerateToken(User user)
    {
        List<Claim> claims =
        [
            new (ClaimTypes.NameIdentifier, user.Id.ToString()),
            new (ClaimTypes.Role, user.Role.Name),
            new (ClaimTypes.Email, user.Email!),
            new ("security_stamp", user.SecurityStamp!)
        ];

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_settings.Key));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _settings.Issuer,
            audience: _settings.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(_settings.DurationInMinutes),
            signingCredentials: creds);

        var next = new JwtSecurityTokenHandler().WriteToken(token);
        return new AccessToken(next,token.ValidTo) ;
    }
    
    public IssuedRefreshToken GenerateRefreshToken()
    {
        var value = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var now = DateTime.UtcNow;

        return new IssuedRefreshToken(value, new RefreshToken
        {
            TokenHash = RefreshTokenHash.Compute(value),
            CreatedAt = now,
            ExpiresAt = now.AddDays(_settings.ExpiryDays)
        });
    }
    
}