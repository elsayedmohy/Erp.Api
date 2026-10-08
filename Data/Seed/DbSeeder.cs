namespace ErpDashboard.Api.Data.Seed;

public static class DbSeeder
{
    public static async Task SeedAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();

        await db.Database.MigrateAsync();

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
                "Admin seeding failed: " +
                string.Join("; ", result.Errors.Select(e => e.Description)));
    }
}