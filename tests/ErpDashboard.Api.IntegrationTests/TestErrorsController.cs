using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ErpDashboard.Api.IntegrationTests;

[ApiController]
[AllowAnonymous]
[Route("__test")]
public sealed class TestErrorsController : ControllerBase
{
    [HttpGet("ok")]
    public IActionResult Ok_() => Ok();

    [HttpGet("throw")]
    public IActionResult Throw() =>
        throw new InvalidOperationException("SECRET-internal-detail");

    [HttpPost("dto")]
    public IActionResult Dto(TestDto dto) => Ok();

}

public sealed record TestDto([property: Required] string? Name);