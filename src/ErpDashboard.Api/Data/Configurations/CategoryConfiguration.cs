namespace ErpDashboard.Api.Data.Configurations;

public class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> b)
    {
        b.Property(c => c.Name).HasMaxLength(100).IsRequired();
        b.HasIndex(c => c.Name).IsUnique();
    }
}
public class CreditNoteConfiguration : IEntityTypeConfiguration<CreditNote>
{
    public void Configure(EntityTypeBuilder<CreditNote> b)
    {
        b.Property(c => c.Number).HasMaxLength(20).IsRequired();
        b.HasIndex(c => c.Number).IsUnique();
        b.Property(c => c.Reason).HasMaxLength(500).IsRequired();
        b.HasIndex(c => c.InvoiceId);

        b.HasMany(c => c.Lines).WithOne()
            .HasForeignKey(l => l.CreditNoteId).OnDelete(DeleteBehavior.Cascade);
    }
}