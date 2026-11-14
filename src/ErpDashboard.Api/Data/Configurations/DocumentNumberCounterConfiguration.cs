namespace ErpDashboard.Api.Data.Configurations;

public class DocumentNumberCounterConfiguration : IEntityTypeConfiguration<DocumentNumberCounter>
{
    public void Configure(EntityTypeBuilder<DocumentNumberCounter> b)
    {
        b.HasKey(c => new { c.Series, c.Year });
        b.Property(c => c.Series).HasMaxLength(10);
        b.ToTable(t => t.HasCheckConstraint("CK_DocumentNumberCounters_Positive", "\"LastNumber\" > 0"));
    }
}