namespace ErpDashboard.Api.Data.Configurations;

public class OrderCheckoutConfiguration : IEntityTypeConfiguration<OrderCheckout>
{
    public void Configure(EntityTypeBuilder<OrderCheckout> b)
    {
        b.Property(c => c.Id).ValueGeneratedNever();
        b.Property(c => c.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(c => c.Governorate).HasConversion<string>().HasMaxLength(30);
        b.Property(c => c.GoodsAmount).HasPrecision(18, 2);
        b.Property(c => c.ShippingAmount).HasPrecision(18, 2);
        b.Property(c => c.TotalAmount).HasPrecision(18, 2);
        b.Property(c => c.CustomerMessage).HasMaxLength(500);
        
        b.HasIndex(c => c.OrderId).IsUnique().HasFilter("\"Status\" = 'AwaitingPayment'");
        b.HasIndex(c => new { c.Status, c.ExpiresAt, c.ExpiryRetryAt });

        b.HasOne<Order>().WithMany().HasForeignKey(c => c.OrderId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<PaymentAttempt>().WithMany().HasForeignKey(c => c.PaymentAttemptId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(c => c.Vehicles).WithOne().HasForeignKey(v => v.CheckoutId).OnDelete(DeleteBehavior.Cascade);
    }
}