namespace ErpDashboard.Api.Data.Configurations;

public class ShipmentConfiguration : IEntityTypeConfiguration<Shipment>
{
    public void Configure(EntityTypeBuilder<Shipment> b)
    {
        b.Property(s => s.Id).ValueGeneratedNever();
        b.Property(s => s.TrackingNumber).HasMaxLength(30).IsRequired();
        b.HasIndex(s => s.TrackingNumber).IsUnique();
        b.HasIndex(s => s.OrderId).IsUnique();
        b.Property(s => s.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(s => s.Governorate).HasConversion<string>().HasMaxLength(30);
        b.Property(s => s.TotalCost).HasPrecision(18, 2);

        b.HasOne<Order>().WithMany().HasForeignKey(s => s.OrderId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(s => s.Vehicles).WithOne().HasForeignKey(v => v.ShipmentId).OnDelete(DeleteBehavior.Cascade);
    }
}