namespace ErpDashboard.Api.Features.Identity.Users;

public static class UserMappings
{
    public static readonly Expression<Func<User, UserResponse>> AsResponse = u => new UserResponse(
        u.Id,
        new RoleSummeryResponse(u.Role.Id, u.Role.Name),
        u.FirstName,
        u.LastName,
        u.Email,
        u.ShowroomId);

    private static readonly Func<User, UserResponse> Compiled = AsResponse.Compile();

    /// The user must be loaded with its Role.
    public static UserResponse ToResponse(this User u) => Compiled(u);
}
