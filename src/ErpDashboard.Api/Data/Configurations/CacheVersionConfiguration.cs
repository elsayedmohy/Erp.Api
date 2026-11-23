namespace ErpDashboard.Api.Data.Configurations;

public class CacheVersionConfiguration : IEntityTypeConfiguration<CacheVersion>
{
    public void Configure(EntityTypeBuilder<CacheVersion> b)
    {
        b.HasKey(v => v.Key);
        b.Property(v => v.Key).HasMaxLength(50);
    }
}
