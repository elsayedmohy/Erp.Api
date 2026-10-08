namespace ErpDashboard.Api.Features.Identity.Users;

public class UserService(AppDbContext dbContext) : IUserService
{
    private static readonly SortMap<User> Sorts = new SortMap<User>()
        .Add("firstName", c => c.FirstName)
        .Add("LastName", c => c.LastName)
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
                c.RoleId,
                c.FirstName,
                c.LastName,
                c.Email)).ToPagedResultAsync(query , ct);
        
    }
}