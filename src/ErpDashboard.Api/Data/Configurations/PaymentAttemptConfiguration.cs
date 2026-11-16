namespace ErpDashboard.Api.Data.Configurations;

public class PaymentAttemptConfiguration : IEntityTypeConfiguration<PaymentAttempt>
{
    public void Configure(EntityTypeBuilder<PaymentAttempt> b)
    {
        b.Property(a => a.Id).ValueGeneratedNever();
        b.Property(a => a.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(a => a.Currency).HasMaxLength(3).IsRequired();
        b.Property(a => a.Provider).HasMaxLength(30).IsRequired();
        b.Property(a => a.ProviderReference).HasMaxLength(100);
        b.Property(a => a.CheckoutUrl).HasMaxLength(2000);
        b.Property(a => a.FailureReason).HasMaxLength(500);
        b.Property(a => a.ProviderTransactionId).HasMaxLength(100);
        
        b.HasIndex(a => new { a.InvoiceId, a.Status });
        b.HasIndex(a => new { a.Provider, a.ProviderReference }).IsUnique()
            .HasFilter("\"ProviderReference\" IS NOT NULL");

        b.ToTable(t =>
        {
            t.HasCheckConstraint("CK_PaymentAttempts_Amount", "\"AmountMinor\" > 0");
            t.HasCheckConstraint("CK_PaymentAttempts_PaysOneThing", "(\"InvoiceId\" IS NULL) <> (\"OrderId\" IS NULL)");
        });

        b.HasOne<Invoice>().WithMany()
            .HasForeignKey(a => a.InvoiceId).OnDelete(DeleteBehavior.Restrict);
        
        b.HasIndex(a => new { a.OrderId, a.Status });
        b.HasOne<Order>().WithMany().HasForeignKey(a => a.OrderId).OnDelete(DeleteBehavior.Restrict);
        
        b.Property(a => a.RefundReference).HasMaxLength(100);
        b.Property(a => a.RefundError).HasMaxLength(500);
        b.HasIndex(a => new { a.Status, a.NextRefundAt });
    }
}