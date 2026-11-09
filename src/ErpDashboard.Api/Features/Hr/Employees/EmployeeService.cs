namespace ErpDashboard.Api.Features.Hr.Employees;

public class EmployeeService(AppDbContext dbContext, 
    ICurrentUserPermissions permissions) : IEmployeeService
{
    private bool CanViewSalary => permissions.Has(PermissionCodes.Employees.ViewSalary);

    private void EnsureCanWriteSalary(decimal? salary)
    {
        if (salary is not null && !CanViewSalary)
            throw new ForbiddenException("You do not have permission to set salaries.");
    }
    private static readonly SortMap<Employee> Sorts = new SortMap<Employee>()
        .Add("firstName", e => e.FirstName)
        .Add("lastName", e => e.LastName)
        .Add("email", e => e.Email)
        .Add("phoneNumber", e => e.PhoneNumber)
        .Add("position", e => e.Position)
        .Add("hireDate", e => e.HireDate)
        .Default("hireDate", descending: true)
        .TieBreaker(e => e.Id);

    public Task<PagedResult<EmployeeResponse>> ListAsync(EmployeeQuery query, CancellationToken ct)
    {
        var canViewSalary = CanViewSalary;
        var q = dbContext.Employees.AsNoTracking();

        if (query.DepartmentId is { } departmentId) q = q.Where(e => e.DepartmentId == departmentId);

        if (query.GetSearchPattern() is { } p)
            q = q.Where(e =>
                EF.Functions.ILike(e.FirstName + " " + e.LastName, p, @"\") ||
                EF.Functions.ILike(e.Email, p, @"\") ||
                EF.Functions.ILike(e.PhoneNumber, p, @"\"));

        return Sorts.Apply(q, query.SortBy, query.SortDir)
            .Select(e => new EmployeeResponse(
                e.Id,
                e.FirstName,
                e.LastName,
                e.Email,
                e.PhoneNumber,
                e.DepartmentId,
                e.Position,
                e.HireDate,
                canViewSalary ? e.Salary : null))
            .ToPagedResultAsync(query, ct);
    }

    public async Task<EmployeeResponse> GetByIdAsync(Guid id, CancellationToken ct)
    {
        Employee? employee = await dbContext.Employees.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (employee is null)
            throw new NotFoundException("Employee", id);

        return ToResponse(employee,canViewSalary:CanViewSalary);
    }

    public async Task<EmployeeResponse> CreateAsync(EmployeeRequest request, CancellationToken ct)
    {
        bool exists = await dbContext.Employees.AnyAsync(e => e.Email == request.Email.Trim().ToLowerInvariant(), ct);
        if (exists)
            throw new ConflictException("The email already exists");


        bool departmentExists = await dbContext.Departments.AnyAsync(d => d.Id == request.DepartmentId, ct);
        if (!departmentExists)
            throw new RequestValidationException("departmentId", $"Department '{request.DepartmentId}' does not exist.");

        EnsureCanWriteSalary(request.Salary);

        if (request.Salary is null)
            throw new RequestValidationException("salary", "Salary is required.");
        
        Employee? employee = new Employee
        {
            FirstName = request.FirstName,
            LastName = request.LastName,
            Email = request.Email,
            PhoneNumber = request.PhoneNumber,
            DepartmentId = request.DepartmentId,
            Position = request.Position,
            HireDate = request.HireDate,
            Salary = request.Salary.Value
        };

        dbContext.Employees.Add(employee);
        await dbContext.SaveChangesAsync(ct);
        return ToResponse(employee , canViewSalary:CanViewSalary);
    }

    public async Task<EmployeeResponse> UpdateAsync(Guid id, UpdateEmployeeRequest request, CancellationToken ct)
    {
        Employee? employee = await dbContext.Employees.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (employee is null)
            throw new NotFoundException("Employee", id);

        if (request.DepartmentId != employee.DepartmentId)
        {
            bool departmentExists = await dbContext.Departments.AnyAsync(d => d.Id == request.DepartmentId, ct);
            if (!departmentExists)
                throw new RequestValidationException("departmentId", $"Department '{request.DepartmentId}' does not exist.");
        }
        
        EnsureCanWriteSalary(request.Salary);

        

        employee.FirstName = request.FirstName;
        employee.LastName = request.LastName;
        employee.PhoneNumber = request.PhoneNumber;
        employee.DepartmentId = request.DepartmentId;
        employee.Position = request.Position;
        employee.HireDate = request.HireDate;
        if (request.Salary is { } salary)
            employee.Salary = salary;
        
        await dbContext.SaveChangesAsync(ct);
        return ToResponse(employee ,canViewSalary:CanViewSalary);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        Employee? employee = await dbContext.Employees.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (employee is null)
            throw new NotFoundException("Employee", id);

        dbContext.Employees.Remove(employee);
        await dbContext.SaveChangesAsync(ct);
    }


    public static EmployeeResponse ToResponse(Employee employee,bool canViewSalary) => new EmployeeResponse(
        employee.Id,
        employee.FirstName,
        employee.LastName,
        employee.Email,
        employee.PhoneNumber,
        employee.DepartmentId,
        employee.Position,
        employee.HireDate,
        canViewSalary ? employee.Salary : null);
}