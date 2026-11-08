namespace ErpDashboard.Api.Controllers;

[ApiController]
[Route("api/profile")]
public class ProfileController(IProfileService profileService) : ControllerBase
{
    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> GetMine()
    {
        var result = await profileService.GetMineAsync(User.GetUserId());
        return Ok(result);
    }
}