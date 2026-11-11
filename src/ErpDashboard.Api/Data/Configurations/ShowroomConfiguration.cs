namespace ErpDashboard.Api.Data.Configurations;

public class ShowroomConfiguration : IEntityTypeConfiguration<Showroom>
{
    public void Configure(EntityTypeBuilder<Showroom> b)
    {
        b.Property(c => c.TaxId).HasMaxLength(50);
        b.HasIndex(c => c.Email).IsUnique().HasFilter("\"IsDeleted\" = false");
        b.HasQueryFilter(QueryFilters.SoftDelete, e => !e.IsDeleted);
    }
}