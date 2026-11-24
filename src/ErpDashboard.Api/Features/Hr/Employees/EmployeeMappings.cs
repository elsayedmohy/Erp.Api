namespace ErpDashboard.Api.Features.Hr.Employees;

public static class EmployeeMappings
{
    /// canViewSalary is captured, so EF sends it as a SQL parameter: one cached query plan serves both
    /// answers, and a user without employees.view_salary never gets the column.
    public static Expression<Func<Employee, EmployeeResponse>> AsResponse(bool canViewSalary) =>
        e => new EmployeeResponse(
            e.Id,
            e.FirstName,
            e.LastName,
            e.Email,
            e.PhoneNumber,
            e.DepartmentId,
            e.Position,
            e.HireDate,
            canViewSalary ? e.Salary : (decimal?)null);

    private static readonly Func<Employee, EmployeeResponse> WithSalary = AsResponse(true).Compile();
    private static readonly Func<Employee, EmployeeResponse> WithoutSalary = AsResponse(false).Compile();

    public static EmployeeResponse ToResponse(this Employee e, bool canViewSalary) =>
        (canViewSalary ? WithSalary : WithoutSalary)(e);
}
