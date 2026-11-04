namespace ErpDashboard.Api.Features.Profiles;

public class ProfileService(
    UserManager<User> userManager,
    AppDbContext dbContext) : IProfileService
{
    public async Task<ProfileResponse> GetMineAsync(Guid userId)
    {
        var profile = await dbContext.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Include(u => u.Role)
            .Select(u => new ProfileResponse(
                u.Id,
                u.FirstName,
                u.LastName,
                u.Email,
                u.PhoneNumber,
                new RoleSummeryResponse(u.Role.Id , u.Role.Name))).FirstOrDefaultAsync();

        if (profile is null)
            throw new Exception("Profile not found");

        return profile;
    }

}