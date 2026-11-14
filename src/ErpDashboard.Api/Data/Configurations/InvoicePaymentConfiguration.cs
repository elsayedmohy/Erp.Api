namespace ErpDashboard.Api.Data.Configurations;

public class InvoicePaymentConfiguration : IEntityTypeConfiguration<InvoicePayment>
{
    public void Configure(EntityTypeBuilder<InvoicePayment> b)
    {
        b.Property(p => p.Reference).HasMaxLength(100);
        b.HasIndex(p => p.InvoiceId);
        b.ToTable(t => t.HasCheckConstraint("CK_InvoicePayments_Amount", "\"Amount\" > 0"));
    }
}