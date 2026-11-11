namespace ErpDashboard.Api.Data.Entities;

public class User : IdentityUser<Guid>
{
    public string FirstName { get; set; }
    public string LastName { get; set; }
    public Guid RoleId { get; set; }
    public Role Role { get; set; } = null!;
    public Guid? ShowroomId { get; set; }
    public ICollection<RefreshToken> RefreshTokens { get; set; } = [];

}