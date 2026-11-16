namespace ErpDashboard.Api.Data.Configurations;

public class CarrierConfiguration : IEntityTypeConfiguration<Carrier>
{
    public void Configure(EntityTypeBuilder<Carrier> b)
    {
        b.Property(c => c.Id).ValueGeneratedNever();
        b.Property(c => c.Name).HasMaxLength(100).IsRequired();
        b.Property(c => c.Phone).HasMaxLength(20);
        b.Property(c => c.Notes).HasMaxLength(500);
        b.HasIndex(c => c.Name).IsUnique();
        b.HasIndex(c => c.IsCompanyFleet).IsUnique().HasFilter("\"IsCompanyFleet\"");

        b.HasMany(c => c.Rates).WithOne().HasForeignKey(r => r.CarrierId).OnDelete(DeleteBehavior.Cascade);

        b.HasData(new Carrier
        {
            Id = CarrierIds.CompanyFleet,
            Name = "Company trucks",
            IsCompanyFleet = true,
            IsActive = true
        });
    }
}