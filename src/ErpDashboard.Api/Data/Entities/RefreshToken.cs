namespace ErpDashboard.Api.Data.Entities;

public class RefreshToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid FamilyId { get; set; }
    public string TokenHash { get; set; } = null!;
    public DateTime ExpiresAt { get; set; }
    public bool  IsExpired  => DateTime.UtcNow >= ExpiresAt;
    public DateTime CreatedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    
    public DateTime? RotatedAt { get; set; }
    
    public bool IsActive => RevokedAt == null &&  !IsExpired;
}