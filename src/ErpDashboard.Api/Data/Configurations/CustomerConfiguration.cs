namespace ErpDashboard.Api.Data.Configurations;

public class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> b)
    {
        b.Property(c => c.TaxId).HasMaxLength(50);
        b.HasIndex(c => c.Email).IsUnique().HasFilter("\"IsDeleted\" = false");
        b.HasQueryFilter(c => !c.IsDeleted);
    }
}