namespace ErpDashboard.Api.Features.Profiles;

public interface IProfileService
{
    Task<ProfileResponse> GetMineAsync(Guid userId);
}