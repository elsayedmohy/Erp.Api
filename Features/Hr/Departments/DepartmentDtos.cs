namespace ErpDashboard.Api.Features.Hr.Departments;


public sealed class DepartmentQuery : PageQuery;

public sealed record DepartmentResponse(
    Guid Id,
    string Name
);

public sealed record DepartmentRequest(
    string Name
);