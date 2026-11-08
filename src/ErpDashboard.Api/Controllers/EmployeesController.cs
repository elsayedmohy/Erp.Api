
namespace ErpDashboard.Api.Controllers;


[ApiController]
[Route("api/[controller]")]
public class EmployeesController(IEmployeeService service) : ControllerBase
{
    [HttpGet, HasPermission(PermissionCodes.Employees.Read)]
    public Task<PagedResult<EmployeeResponse>> List([FromQuery] EmployeeQuery query, CancellationToken ct)
        => service.ListAsync(query, ct);
    
    
    [HttpGet("{id:guid}"), HasPermission(PermissionCodes.Employees.Read)]
    public Task<EmployeeResponse> GetById( Guid id, CancellationToken ct)
        => service.GetByIdAsync(id, ct);
    
    [HttpPost, HasPermission(PermissionCodes.Employees.Write)]
    public async Task<ActionResult<EmployeeResponse>> Create([FromBody] EmployeeRequest request, CancellationToken ct)
    {
        var result = await service.CreateAsync(request, ct);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpPut("{id:guid}"), HasPermission(PermissionCodes.Employees.Write)]
    public async Task<ActionResult<EmployeeResponse>> Update(Guid id, [FromBody] UpdateEmployeeRequest request, CancellationToken ct)
        => Ok(await service.UpdateAsync(id, request, ct));

    [HttpDelete("{id:guid}"), HasPermission(PermissionCodes.Employees.Write)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await service.DeleteAsync(id, ct);
        return NoContent();
    }
}

