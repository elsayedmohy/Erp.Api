
namespace ErpDashboard.Api.Auth.Token;

public class TokenService(IOptions<JwtSettings> settings) : ITokenService
{
    private readonly JwtSettings _settings = settings.Value;

    public AccessToken GenerateToken(User user)
    {
        List<Claim> claims =
        [
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Role, user.Role.Name),
            new Claim(ClaimTypes.Email, user.Email!)
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
    
    public RefreshToken GenerateRefreshToken()
    {
        byte[] randomBytes = RandomNumberGenerator.GetBytes(32);
        var base64String=  Convert.ToBase64String(randomBytes);
        return new RefreshToken
        {
            Token = base64String,
            
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(_settings.ExpiryDays)
        };
    }
    
}