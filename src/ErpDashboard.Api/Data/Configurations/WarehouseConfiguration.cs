namespace ErpDashboard.Api.Data.Configurations;

public class WarehouseConfiguration : IEntityTypeConfiguration<Warehouse>
{
    public void Configure(EntityTypeBuilder<Warehouse> b)
    {
        b.Property(w => w.Code).HasMaxLength(20).IsRequired();
        b.Property(w => w.Name).HasMaxLength(100).IsRequired();
        b.HasIndex(w => w.Code).IsUnique();
    }
    
}