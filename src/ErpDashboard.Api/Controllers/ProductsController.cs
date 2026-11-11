namespace ErpDashboard.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ProductsController(IProductService service) : ControllerBase
{
    [HttpGet, HasPermission(PermissionCodes.Products.Read)]
    public Task<PagedResult<ProductResponse>> List([FromQuery] ProductQuery query, CancellationToken ct)
        => service.ListAsync(query, ct);


    [HttpGet("{id:guid}"), HasPermission(PermissionCodes.Products.Read)]
    public Task<ProductDetailsResponse> GetById(Guid id, CancellationToken ct)
        => service.GetByIdAsync(id, ct);

    [HttpPost, HasPermission(PermissionCodes.Products.Write)]
    public async Task<ActionResult<ProductResponse>> Create([FromBody] ProductRequest request, CancellationToken ct)
    {
        var result = await service.CreateAsync(request, ct);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpPut("{id:guid}"), HasPermission(PermissionCodes.Products.Write)]
    public async Task<ActionResult<ProductResponse>> Update(Guid id, [FromBody] ProductRequest request,
        CancellationToken ct)
        => Ok(await service.UpdateAsync(id, request, User.GetUserId(), ct));

    [HttpDelete("{id:guid}"), HasPermission(PermissionCodes.Products.Write)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await service.DeleteAsync(id, ct);
        return NoContent();
    }
}