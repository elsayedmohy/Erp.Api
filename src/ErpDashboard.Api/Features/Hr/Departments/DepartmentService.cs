namespace ErpDashboard.Api.Features.Hr.Departments;

public class DepartmentService(AppDbContext dbContext) : IDepartmentService
{
    private static readonly SortMap<Department> Sorts = new SortMap<Department>()
        .Add("name", d => d.Name)
        .Default("name")
        .TieBreaker(d => d.Id);

    public Task<PagedResult<DepartmentResponse>> ListAsync(DepartmentQuery query, CancellationToken ct)
    {
        var q = dbContext.Departments.AsNoTracking();

        if (query.GetSearchPattern() is { } p)
            q = q.Where(d =>
                EF.Functions.ILike(d.Name, p, @"\"));

        return Sorts.Apply(q, query.SortBy, query.SortDir)
            .Select(d => new DepartmentResponse(
                d.Id,
                d.Name))
            .ToPagedResultAsync(query, ct);
    }


    public async Task<DepartmentResponse> GetByIdAsync(Guid id, CancellationToken ct)
    {
        Department? department = await dbContext.Departments.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (department is null)
            throw new NotFoundException("Department", id);

        return new DepartmentResponse(department.Id, department.Name);
    }

    public async Task<DepartmentResponse> CreateAsync(DepartmentRequest request, CancellationToken ct)
    {
        Department department = new Department
        {
            Name = request.Name,
        };

        dbContext.Departments.Add(department);
        await dbContext.SaveChangesAsync(ct);
        return new DepartmentResponse(department.Id, department.Name);
    }

    public async Task<DepartmentResponse> UpdateAsync(Guid id, DepartmentRequest request, CancellationToken ct)
    {
       Department? department = await dbContext.Departments.FirstOrDefaultAsync(d => d.Id == id, ct);
       if (department is null)
           throw new NotFoundException("Department", id);
       
       department.Name = request.Name;
       await dbContext.SaveChangesAsync(ct);
       return new DepartmentResponse(department.Id, department.Name);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        Department? department = await dbContext.Departments.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (department is null)
            throw new NotFoundException("Department", id);

        bool employeesExist = await dbContext.Employees.IgnoreQueryFilters().AnyAsync(p => p.DepartmentId == id, ct);
        if (employeesExist)
            throw new BadRequestException("Cannot delete Department contains Employees");


        dbContext.Departments.Remove(department);
        await dbContext.SaveChangesAsync(ct);
    }
}