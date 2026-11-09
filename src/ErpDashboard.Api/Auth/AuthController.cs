namespace ErpDashboard.Api.Auth;

[ApiController]
[Route("api/auth")]
public class AuthController(IAuthService service): ControllerBase
{
    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequestDto dto)
    {
        var result = await service.LoginAsync(dto);
        return Ok(result);
    }

    [AllowAnonymous]
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh(RefreshTokenRequestDto dto)
    {
      var result =   await service.RefreshTokenAsync(dto.RefreshToken);
      return  Ok(result);
    }
    [Authorize]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(RefreshTokenRequestDto dto)
    {
        await service.LogoutAsync(dto.RefreshToken);
        return NoContent();
    }
    
    [Authorize]
    [HttpPost("revoke-all-sessions")]
    public async Task<IActionResult> RevokeAllSessions()
    {
        await service.RevokeAllSessionsAsync(User);

        return NoContent();
    }
}