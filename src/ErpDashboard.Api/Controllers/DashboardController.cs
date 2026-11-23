using ErpDashboard.Api.Features.Dashboard;

namespace ErpDashboard.Api.Controllers;

[ApiController]
[Route("api/dashboard")]
public class DashboardController(IDashboardService dashboard) : ControllerBase
{
    [HttpGet, HasPermission(PermissionCodes.Dashboard.Read)]
    public Task<DashboardResponse> Get(CancellationToken ct) => dashboard.GetAsync(ct);
}