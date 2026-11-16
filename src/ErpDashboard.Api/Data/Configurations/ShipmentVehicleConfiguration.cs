namespace ErpDashboard.Api.Data.Configurations;

public class ShipmentVehicleConfiguration : IEntityTypeConfiguration<ShipmentVehicle>
{
    public void Configure(EntityTypeBuilder<ShipmentVehicle> b)
    {
        b.Property(v => v.Id).ValueGeneratedNever();
        b.Property(v => v.CarrierName).HasMaxLength(100).IsRequired();
        b.Property(v => v.Price).HasPrecision(18, 2);
        b.Property(v => v.PlateNumber).HasMaxLength(20);
        b.Property(v => v.DriverPhone).HasMaxLength(20);
        b.HasOne<Carrier>().WithMany().HasForeignKey(v => v.CarrierId).OnDelete(DeleteBehavior.Restrict);
        b.ToTable(t => t.HasCheckConstraint("CK_ShipmentVehicles_Price_Positive", "\"Price\" > 0"));
    }
}