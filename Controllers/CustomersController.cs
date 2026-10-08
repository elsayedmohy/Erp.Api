
namespace ErpDashboard.Api.Controllers;


[ApiController]
[Route("api/[controller]")]
public class CustomersController(ICustomerService service) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<CustomerResponse>> List([FromQuery] CustomerQuery query, CancellationToken ct)
        => service.ListAsync(query, ct);
}