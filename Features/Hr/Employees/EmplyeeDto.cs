namespace ErpDashboard.Api.Features.Hr.Employees;

public sealed class EmployeeQuery : PageQuery
{
    public Guid DepartmentId;
    public string Position;
}

public sealed record EmployeeResponse(
    Guid Id,
    string FirstName,
    string LastName,
    string Email,
    string PhoneNumber,
    Guid DepartmentId,
    string Position,
    DateOnly HireDate,
    decimal Salary
);

public sealed record EmployeeRequest(
    string FirstName,
    string LastName,
    string Email,
    string PhoneNumber,
    Guid DepartmentId,
    string Position,
    DateOnly HireDate,
    decimal Salary
);

public sealed record UpdateEmployeeRequest(
    string FirstName,
    string LastName,
    string PhoneNumber,
    Guid DepartmentId,
    string Position,
    DateOnly HireDate,
    decimal Salary
);