using ErpDashboard.Api.Common.Security;

namespace ErpDashboard.Api.Auth;

[ApiController]
[Route("api/auth")]
public class AuthController(IAuthService service): ControllerBase
{
    [AllowAnonymous, EnableRateLimiting(RateLimitPolicies.Login)]
    [HttpPost("login")]
    [ProducesResponseType<LoginDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    public async Task<IActionResult> Login(LoginRequestDto dto)
    {
        var result = await service.LoginAsync(dto);
        return Ok(result);
    }

    [AllowAnonymous, EnableRateLimiting(RateLimitPolicies.Refresh)]
    [HttpPost("refresh")]
    [ProducesResponseType<LoginDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    public async Task<IActionResult> Refresh(RefreshTokenRequestDto dto)
    {
      var result =   await service.RefreshTokenAsync(dto.RefreshToken);
      return  Ok(result);
    }
    [Authorize]
    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout(RefreshTokenRequestDto dto)
    {
        await service.LogoutAsync(dto.RefreshToken);
        return NoContent();
    }
    
    [Authorize]
    [HttpPost("revoke-all-sessions")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RevokeAllSessions()
    {
        await service.RevokeAllSessionsAsync(User);

        return NoContent();
    }
}