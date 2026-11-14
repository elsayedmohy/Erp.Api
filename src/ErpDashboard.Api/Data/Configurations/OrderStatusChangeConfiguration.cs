namespace ErpDashboard.Api.Data.Configurations;

public class OrderStatusChangeConfiguration : IEntityTypeConfiguration<OrderStatusChange>
{
    public void Configure(EntityTypeBuilder<OrderStatusChange> b)
    {
        b.Property(c => c.Reason).HasMaxLength(500);
        b.HasIndex(c => new { c.OrderId, c.ChangedAt });
        b.HasOne<Order>().WithMany()
            .HasForeignKey(c => c.OrderId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}