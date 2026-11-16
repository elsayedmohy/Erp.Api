namespace ErpDashboard.Api.Data.Configurations;

public class OrderCheckoutVehicleConfiguration : IEntityTypeConfiguration<OrderCheckoutVehicle>
{
    public void Configure(EntityTypeBuilder<OrderCheckoutVehicle> b)
    {
        b.Property(v => v.Id).ValueGeneratedNever();
        b.Property(v => v.CarrierName).HasMaxLength(100).IsRequired();
        b.Property(v => v.Price).HasPrecision(18, 2);
        b.HasOne<Carrier>().WithMany().HasForeignKey(v => v.CarrierId).OnDelete(DeleteBehavior.Restrict);
    }
}