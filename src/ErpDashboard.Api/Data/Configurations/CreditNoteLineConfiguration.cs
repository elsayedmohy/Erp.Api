namespace ErpDashboard.Api.Data.Configurations;

public class CreditNoteLineConfiguration : IEntityTypeConfiguration<CreditNoteLine>
{
    public void Configure(EntityTypeBuilder<CreditNoteLine> b)
    {
        b.HasIndex(l => l.InvoiceLineId);
        b.ToTable(t => t.HasCheckConstraint("CK_CreditNoteLines_NetAmount", "\"NetAmount\" > 0"));
        b.HasOne<InvoiceLine>().WithMany()
            .HasForeignKey(l => l.InvoiceLineId).OnDelete(DeleteBehavior.Restrict);
    }
}