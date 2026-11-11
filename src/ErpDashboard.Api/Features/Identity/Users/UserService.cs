namespace ErpDashboard.Api.Features.Identity.Users;

public class UserService(AppDbContext dbContext, UserManager<User> userManager , IWarehouseScope scope) : IUserService
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
                c.Email,
                c.ShowroomId)).ToPagedResultAsync(query, ct);
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
        
        await ValidateShowroomAsync(request.ShowroomId, ct);
        
        User user = new User
        {
            FirstName = request.FirstName,
            LastName = request.LastName,
            Email = request.Email,
            UserName = request.Email,
            RoleId = request.RoleId,
            Role = role,
            ShowroomId = request.ShowroomId
        };
        var result = await userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            throw result.ToValidationException();
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
        
        if (user.RoleId == RoleIds.Admin
            && await dbContext.Users.CountAsync(u => u.RoleId == RoleIds.Admin, ct) <= 1)
            throw new ConflictException("Cannot delete the last administrator.");
        
        await ValidateShowroomAsync(request.ShowroomId, ct);
        
        user.FirstName = request.FirstName;
        user.LastName = request.LastName;
        user.RoleId = request.RoleId;
        user.Role = role;
        user.ShowroomId = request.ShowroomId;
        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            throw result.ToValidationException();
        }
        return ToResponse(user);
    }

    public async Task DeleteUserAsync(Guid id, CancellationToken ct)
    {
        var user = await dbContext.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null)
            throw new NotFoundException("User", id);
        
        if (user.RoleId == RoleIds.Admin
            && await dbContext.Users.CountAsync(u => u.RoleId == RoleIds.Admin, ct) <= 1)
            throw new ConflictException("Cannot delete the last administrator.");
        
        dbContext.Users.Remove(user);
        await dbContext.SaveChangesAsync(ct);
    }
    
    public async Task<UserWarehousesResponse> GetWarehousesAsync(Guid userId, CancellationToken ct)
    {
        if (!await dbContext.Users.AnyAsync(u => u.Id == userId, ct))
            throw new NotFoundException("User", userId);

        var accessible = scope.AccessibleWarehouseIds();
        var ids = await dbContext.UserWarehouses
            .Where(x => x.UserId == userId && accessible.Contains(x.WarehouseId))
            .Select(x => x.WarehouseId)
            .ToListAsync(ct);

        return new UserWarehousesResponse(userId, ids);
    }

    public async Task<UserWarehousesResponse> SetWarehousesAsync(
        Guid userId, UserWarehousesRequest request, CancellationToken ct)
    {
        if (!await dbContext.Users.AnyAsync(u => u.Id == userId, ct))
            throw new NotFoundException("User", userId);

        var ids = request.WarehouseIds.Distinct().ToList();

        var existing = await dbContext.Warehouses
            .Where(w => ids.Contains(w.Id))
            .Select(w => w.Id)
            .ToListAsync(ct);
        var missing = ids.Except(existing).ToList();
        if (missing.Count > 0)
            throw new RequestValidationException("warehouseIds", $"Warehouses not found: {string.Join(", ", missing)}");

        var accessible = scope.AccessibleWarehouseIds();
        var grantable = await accessible.Where(id => ids.Contains(id)).ToListAsync(ct);
        var notAllowed = ids.Except(grantable).ToList();
        if (notAllowed.Count > 0)
            throw new ForbiddenException("You cannot assign warehouses you do not have access to.");

        await using var transaction = await dbContext.Database.BeginTransactionAsync(ct);

        await dbContext.UserWarehouses
            .Where(x => x.UserId == userId && accessible.Contains(x.WarehouseId))
            .ExecuteDeleteAsync(ct);

        dbContext.UserWarehouses.AddRange(ids.Select(id => new UserWarehouse { UserId = userId, WarehouseId = id }));
        await dbContext.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return await GetWarehousesAsync(userId, ct);
    }

    private async Task ValidateShowroomAsync(Guid? showroomId, CancellationToken ct)
    {
        if (showroomId is { } id && !await dbContext.Showrooms.AnyAsync(s => s.Id == id, ct))
            throw new RequestValidationException("showroomId", $"Showroom '{id}' does not exist.");
    }
    
    private static UserResponse ToResponse(User user) =>
        new UserResponse(user.Id,
            new RoleSummeryResponse(user.Role.Id, user.Role.Name),
            user.FirstName, user.LastName, user.Email,
            user.ShowroomId);
}