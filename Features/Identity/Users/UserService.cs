namespace ErpDashboard.Api.Features.Identity.Users;

public class UserService(AppDbContext dbContext) : IUserService
{
    private static readonly SortMap<User> Sorts = new SortMap<User>()
        .Add("firstName", c => c.FirstName)
        .Add("lastName", c => c.LastName)
        .Add("email", c => c.Email)
        .Default("firstName")
        .TieBreaker(c => c.Id);

    public Task<PagedResult<UserResponse>> ListAsync(UserQuery query, CancellationToken ct)
    {
        var q = dbContext.Users.AsNoTracking();

        if (query.GetSearchPattern() is { } p)
            q = q.Where(c =>
                EF.Functions.ILike(c.FirstName, p, @"\") ||
                EF.Functions.ILike(c.LastName, p, @"\") ||
                EF.Functions.ILike(c.Email, p, @"\"));

        return Sorts.Apply(q, query.SortBy, query.SortDir)
            .Select(c => new UserResponse(
                c.Id,
                new RoleSummeryResponse(c.Role.Id , c.Role.Name),
                c.FirstName,
                c.LastName,
                c.Email)).ToPagedResultAsync(query, ct);
    }

    public async Task<UserResponse> GetByIdAsync(Guid id, CancellationToken ct)
    {
        var user = await dbContext.Users.Include(u => u.Role).FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null)
            throw new NotFoundException("User", id);

        return new UserResponse(
            user.Id,
            new RoleSummeryResponse(user.Role.Id , user.Role.Name),
            user.FirstName,
            user.LastName,
            user.Email!);
    }

    public Task<UserResponse> AddUserAsync(UserRequest request)
    {
        throw new NotImplementedException();
    }

    public Task<UserResponse> UpdateUserAsync(Guid id, UserRequest request)
    {
        throw new NotImplementedException();
    }

    public async Task DeleteUserAsync(Guid id, CancellationToken ct)
    {
        var user = await dbContext.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null)
            throw new NotFoundException("User", id);
        dbContext.Users.Remove(user);
        await dbContext.SaveChangesAsync(ct);
    }
}