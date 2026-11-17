namespace ErpDashboard.Api.Data.Configurations;

public class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> b)
    {
        b.Property(n => n.Id).ValueGeneratedNever();
        b.Property(n => n.Channel).HasMaxLength(20).IsRequired();
        b.Property(n => n.Recipient).HasMaxLength(20);
        b.Property(n => n.Body).HasMaxLength(1000).IsRequired();
        b.Property(n => n.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(n => n.ProviderMessageId).HasMaxLength(100);
        b.Property(n => n.Error).HasMaxLength(500);
        b.HasIndex(n => new { n.Status, n.NextAttemptAt });
        b.HasIndex(n => n.CheckoutId);
        b.HasOne<OrderCheckout>().WithMany().HasForeignKey(n => n.CheckoutId).OnDelete(DeleteBehavior.Restrict);
    }
}