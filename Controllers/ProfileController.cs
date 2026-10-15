namespace ErpDashboard.Api.Controllers;

[ApiController]
[Route("api/profile")]
[Authorize]
public class ProfileController(IProfileService profileService) : ControllerBase
{
    [HttpGet("me")]
    public async Task<IActionResult> GetMine()
    {
        var result = await profileService.GetMineAsync(User.GetUserId());
        return Ok(result);
    }

    
}