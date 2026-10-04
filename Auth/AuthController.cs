namespace ErpDashboard.Api.Auth;

[ApiController]
[Route("api/auth")]
public class AuthController(IAuthService service): ControllerBase
{

    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequestDto dto)
    {
        var result = await service.LoginAsync(dto);
        return Ok(result);
    }
    
    [HttpPost("register")]
    public async Task Register(RegisterRequestDto dto)
    {
        await service.RegisterAsync(dto);
    }
    
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh(RefreshTokenRequestDto dto)
    {
      var result =   await service.RefreshTokenAsync(dto.RefreshToken);
      return  Ok(result);
    }
    
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(RefreshTokenRequestDto dto)
    {
        await service.LogoutAsync(dto.RefreshToken);
        return NoContent();
    }
}