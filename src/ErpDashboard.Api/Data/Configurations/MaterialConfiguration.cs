namespace ErpDashboard.Api.Data.Configurations;

public class MaterialConfiguration : IEntityTypeConfiguration<Material>
{
    public void Configure(EntityTypeBuilder<Material> b)
    {
        b.Property(m => m.Name).HasMaxLength(100).IsRequired();
        b.HasIndex(m => m.Name).IsUnique();
    }
}