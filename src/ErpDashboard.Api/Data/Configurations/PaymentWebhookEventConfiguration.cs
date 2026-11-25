namespace ErpDashboard.Api.Data.Configurations;

public class PaymentWebhookEventConfiguration : IEntityTypeConfiguration<PaymentWebhookEvent>
{
    public void Configure(EntityTypeBuilder<PaymentWebhookEvent> b)
    {
        b.Property(e => e.Id).ValueGeneratedNever();
        b.Property(e => e.Provider).HasMaxLength(30).IsRequired();
        b.Property(e => e.RejectionReason).HasMaxLength(200);
        b.Property(e => e.Payload).IsRequired();
        b.Property(e => e.ProviderEventId).HasMaxLength(100);
        b.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(e => e.Result).HasMaxLength(500);

        b.HasIndex(e => new { e.Status, e.NextAttemptAt });
        b.HasIndex(e => e.ReceivedAt);
        b.HasIndex(e => new { e.Provider, e.ProviderEventId });
        b.Property(e => e.Currency).HasMaxLength(3);
        b.Property(a => a.ResolutionNote).HasMaxLength(500);
    }
}