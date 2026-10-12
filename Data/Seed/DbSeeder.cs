namespace ErpDashboard.Api.Data.Seed;

public static class DbSeeder
{
    private sealed record SystemRole(Guid Id, string Name, List<string> Permissions);

    private static readonly SystemRole[] SystemRoles =
    [
        new(RoleIds.Admin, RoleNames.Admin, RolePermissions.Admin),
        new(RoleIds.Manager, RoleNames.Manager, RolePermissions.Manager),
        new(RoleIds.InventoryManager, RoleNames.InventoryManager, RolePermissions.InventoryManager),
        new(RoleIds.Accountant, RoleNames.Accountant, RolePermissions.Accountant),
        new(RoleIds.User, RoleNames.User, RolePermissions.User),
    ];


    public static async Task SeedAsync(IServiceProvider services, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();


        await db.Database.MigrateAsync(ct);
        await EnsureSystemRolesAsync(db, ct);
        await SeedAdminAsync(userManager, config);
    }

    private static async Task EnsureSystemRolesAsync(AppDbContext db, CancellationToken ct)
    {
        var ids = SystemRoles.Select(r => r.Id).ToList();
        var existing = await db.Roles
            .Where(r => ids.Contains(r.Id))
            .ToDictionaryAsync(r => r.Id, ct);

        foreach (var def in SystemRoles)
        {
            if (!existing.TryGetValue(def.Id, out var role))
            {
                db.Roles.Add(new Role
                {
                    Id = def.Id,
                    Name = def.Name,
                    IsSystem = true,
                    Permissions = [.. def.Permissions]
                });
                continue;
            }

            role.Name = def.Name;
            role.IsSystem = true;
            if (!role.Permissions.SequenceEqual(def.Permissions))
                role.Permissions = [.. def.Permissions];
        }

        await db.SaveChangesAsync(ct);
    }


    private static async Task SeedAdminAsync(UserManager<User> userManager, IConfiguration config)
    {
        var email = config["Seed:AdminEmail"] ?? "admin@company.com";
        if (await userManager.FindByEmailAsync(email) is not null) return;

        var password = config["Seed:AdminPassword"]
                       ?? throw new InvalidOperationException("Seed:AdminPassword is not configured.");

        var admin = new User
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            FirstName = "System",
            LastName = "Admin",
            RoleId = RoleIds.Admin
        };

        var result = await userManager.CreateAsync(admin, password);
        if (!result.Succeeded)
            throw new InvalidOperationException(
                "Admin seeding failed: " + string.Join("; ", result.Errors.Select(e => e.Description)));
    }
}