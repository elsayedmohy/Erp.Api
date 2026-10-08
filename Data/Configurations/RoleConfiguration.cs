namespace ErpDashboard.Api.Data.Configurations;

public class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> b)
    {
        b.Property(r => r.Name).HasMaxLength(50).IsRequired();
        b.HasIndex(r => r.Name).IsUnique();
        b.Property(r => r.Permissions).HasColumnType("jsonb");

        b.HasMany(r => r.Users)
            .WithOne(u => u.Role)
            .HasForeignKey(u => u.RoleId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasData(
            new Role { Id = RoleIds.Admin, Name = RoleNames.Admin },
            new Role { Id = RoleIds.Manager, Name = RoleNames.Manager },
            new Role { Id = RoleIds.InventoryManager, Name = RoleNames.InventoryManager },
            new Role { Id = RoleIds.Accountant, Name = RoleNames.Accountant },
            new Role { Id = RoleIds.User, Name = RoleNames.User });
    }
}