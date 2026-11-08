
namespace ErpDashboard.Api.Controllers;


[ApiController]
[Route("api/[controller]")]
public class DepartmentsController(IDepartmentService service) : ControllerBase
{
    [HttpGet, HasPermission(PermissionCodes.Departments.Read)]
    public Task<PagedResult<DepartmentResponse>> List([FromQuery] DepartmentQuery query, CancellationToken ct)
        => service.ListAsync(query, ct);
    
    
    [HttpGet("{id:guid}"), HasPermission(PermissionCodes.Departments.Read)]
    public Task<DepartmentResponse> GetById( Guid id, CancellationToken ct)
        => service.GetByIdAsync(id, ct);
    
    [HttpPost, HasPermission(PermissionCodes.Departments.Write)]
    public async Task<ActionResult<DepartmentResponse>> Create([FromBody] DepartmentRequest request, CancellationToken ct)
    {
        var result = await service.CreateAsync(request, ct);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpPut("{id:guid}"), HasPermission(PermissionCodes.Departments.Write)]
    public async Task<ActionResult<DepartmentResponse>> Update(Guid id, [FromBody] DepartmentRequest request, CancellationToken ct)
        => Ok(await service.UpdateAsync(id, request, ct));

    [HttpDelete("{id:guid}"), HasPermission(PermissionCodes.Departments.Write)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await service.DeleteAsync(id, ct);
        return NoContent();
    }
}

