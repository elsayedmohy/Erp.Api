namespace ErpDashboard.Api.Data.Configurations;

public class ExportJobConfiguration : IEntityTypeConfiguration<ExportJob>
{
    public void Configure(EntityTypeBuilder<ExportJob> b)
    {
        b.Property(j => j.Kind).HasConversion<string>().HasMaxLength(20);
        b.Property(j => j.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(j => j.Error).HasMaxLength(500);

        // The worker's lease query: what is waiting, or abandoned, oldest first.
        b.HasIndex(j => new { j.Status, j.CreatedAt });
        // "Is the same export already queued for this user?"
        b.HasIndex(j => new { j.RequestedById, j.Kind, j.Year, j.Status });
    }
}
