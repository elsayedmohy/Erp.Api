namespace ErpDashboard.Api.Data.Configurations;

public class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> b)
    {
        b.Property(r => r.Name).HasMaxLength(50).IsRequired();
        b.HasIndex(r => r.Name).IsUnique();

        b.HasMany(r => r.Users)
            .WithOne(u => u.Role)
            .HasForeignKey(u => u.RoleId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}