namespace ErpDashboard.Api.Features.Identity.Users;

public class UserService(AppDbContext dbContext, UserManager<User> userManager) : IUserService
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
                new RoleSummeryResponse(c.Role.Id, c.Role.Name),
                c.FirstName,
                c.LastName,
                c.Email)).ToPagedResultAsync(query, ct);
    }

    public async Task<UserResponse> GetByIdAsync(Guid id, CancellationToken ct)
    {
        var user = await dbContext.Users.Include(u => u.Role).FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null)
            throw new NotFoundException("User", id);

        return ToResponse(user);
    }

    public async Task<UserResponse> CreateAsync(UserRequest request, CancellationToken ct)
    {

        Role? role =  await dbContext.Roles.FirstOrDefaultAsync(r => r.Id == request.RoleId, ct);
        if (role is null)
            throw new NotFoundException("Role", request.RoleId);
        
        
        User user = new User
        {
            FirstName = request.FirstName,
            LastName = request.LastName,
            Email = request.Email,
            UserName = request.Email,
            RoleId = request.RoleId,
            Role = role
        };
        var result = await userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            throw new BadRequestException(result.Errors.First().Description);
        }
        return ToResponse(user);
    }

    public async Task<UserResponse> UpdateAsync(UpdateUserRequest request, Guid id, CancellationToken ct)
    {
        var user = await dbContext.Users.Include(u => u.Role).FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null)
            throw new NotFoundException("User", id);
        
        Role? role =  await dbContext.Roles.FirstOrDefaultAsync(r => r.Id == request.RoleId, ct);
        if (role is null)
            throw new NotFoundException("Role", request.RoleId);

        int count = await dbContext.Users.CountAsync(u => u.Role.Id == RoleIds.Admin  ,ct);
        
        if (user.Role.Name == RoleNames.Admin && role.Id == RoleIds.Admin &&  count <= 1)
        {
            throw new BadRequestException("Cannot remove the Admin role from the last administrator");
        }
        
        user.FirstName = request.FirstName;
        user.LastName = request.LastName;
        user.RoleId = request.RoleId;
        user.Role = role;
        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            throw new BadRequestException(result.Errors.First().Description);
        }
        return ToResponse(user);
    }

    public async Task DeleteUserAsync(Guid id, CancellationToken ct)
    {
        var user = await dbContext.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null)
            throw new NotFoundException("User", id);
        dbContext.Users.Remove(user);
        await dbContext.SaveChangesAsync(ct);
    }


    private static UserResponse ToResponse(User user) =>
        new UserResponse(user.Id,
            new RoleSummeryResponse(user.Role.Id, user.Role.Name),
            user.FirstName, user.LastName, user.Email);
}