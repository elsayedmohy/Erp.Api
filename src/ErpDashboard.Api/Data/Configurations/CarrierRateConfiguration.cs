namespace ErpDashboard.Api.Data.Configurations;

public class CarrierRateConfiguration : IEntityTypeConfiguration<CarrierRate>
{
    public void Configure(EntityTypeBuilder<CarrierRate> b)
    {
        b.Property(r => r.Id).ValueGeneratedNever();
        b.Property(r => r.Governorate).HasConversion<string>().HasMaxLength(30);
        b.Property(r => r.VehiclePrice).HasPrecision(18, 2);
        b.HasIndex(r => new { r.CarrierId, r.Governorate }).IsUnique();
        b.ToTable(t => t.HasCheckConstraint("CK_CarrierRates_VehiclePrice_Positive", "\"VehiclePrice\" > 0"));
    }
}