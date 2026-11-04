using System.ComponentModel.DataAnnotations;
using ErpDashboard.Api.Common;
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

    [HttpGet("validation")]
    public IActionResult Validation() =>
        throw new RequestValidationException("name", "Name is invalid.");

    [HttpGet("conflict")]
    public IActionResult Conflict_() =>
        throw new ConflictException("Already exists.");
}

public sealed record TestDto([Required] string? Name);