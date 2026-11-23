namespace ErpDashboard.Api.Data.Configurations;

public class CreditNoteConfiguration : IEntityTypeConfiguration<CreditNote>
{
    public void Configure(EntityTypeBuilder<CreditNote> b)
    {
        b.Property(c => c.Number).HasMaxLength(20).IsRequired();
        b.HasIndex(c => c.Number).IsUnique();
        b.Property(c => c.Reason).HasMaxLength(500).IsRequired();
        b.HasIndex(c => c.InvoiceId).IncludeProperties(c => c.TotalAmount);
        b.HasMany(c => c.Lines).WithOne()
            .HasForeignKey(l => l.CreditNoteId).OnDelete(DeleteBehavior.Cascade);
    }
}