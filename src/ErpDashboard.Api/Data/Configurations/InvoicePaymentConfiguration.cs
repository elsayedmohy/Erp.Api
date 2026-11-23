namespace ErpDashboard.Api.Data.Configurations;

public class InvoicePaymentConfiguration : IEntityTypeConfiguration<InvoicePayment>
{
    public void Configure(EntityTypeBuilder<InvoicePayment> b)
    {
        b.Property(p => p.Reference).HasMaxLength(100);
        b.HasIndex(p => p.InvoiceId).IncludeProperties(p => p.Amount);
        b.ToTable(t => t.HasCheckConstraint("CK_InvoicePayments_Amount", "\"Amount\" > 0"));
        
        b.HasOne<PaymentAttempt>().WithMany()
            .HasForeignKey(p => p.PaymentAttemptId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(p => p.PaymentAttemptId).IsUnique()
            .HasFilter("\"PaymentAttemptId\" IS NOT NULL");
    }
}