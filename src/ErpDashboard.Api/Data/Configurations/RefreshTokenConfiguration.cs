namespace ErpDashboard.Api.Data.Configurations;

public class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> b)
    {
        b.Property(c => c.TokenHash).HasMaxLength(64).IsRequired();
        b.HasIndex(c => c.TokenHash).IsUnique();
    }
}