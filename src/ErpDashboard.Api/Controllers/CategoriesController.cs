

namespace ErpDashboard.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CategoriesController(ICategoryService service) : ControllerBase
{
    [HttpGet, HasPermission(PermissionCodes.Categories.Read)]
    public Task<PagedResult<CategoryResponse>> List([FromQuery] CategoryQuery query, CancellationToken ct)
        => service.ListAsync(query, ct);
    
    
    [HttpGet("{id:guid}"), HasPermission(PermissionCodes.Categories.Read)]
    public Task<CategoryResponse> GetById( Guid id, CancellationToken ct)
        => service.GetByIdAsync(id, ct);
    
    [HttpPost, HasPermission(PermissionCodes.Categories.Write)]
    public async Task<ActionResult<CategoryResponse>> Create([FromBody] CategoryRequest request, CancellationToken ct)
    {
        var result = await service.CreateAsync(request, ct);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpPut("{id:guid}"), HasPermission(PermissionCodes.Categories.Write)]
    public async Task<ActionResult<CategoryResponse>> Update(Guid id, [FromBody] CategoryRequest request, CancellationToken ct)
        => Ok(await service.UpdateAsync(id, request, ct));

    [HttpDelete("{id:guid}"), HasPermission(PermissionCodes.Categories.Write)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await service.DeleteAsync(id, ct);
        return NoContent();
    }
}