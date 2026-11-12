namespace ErpDashboard.Api.Data.Configurations;

public class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> b)
    {
        b.HasOne<Showroom>(o => o.Showroom)
            .WithMany(c => c.Orders)
            .HasForeignKey(o => o.ShowroomId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasMany(o => o.OrderLines)
            .WithOne()
            .HasForeignKey(l => l.OrderId)
            .OnDelete(DeleteBehavior.Cascade);
        
        b.HasOne<Warehouse>().WithMany()
            .HasForeignKey(o => o.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        b.Property(o => o.Notes).HasMaxLength(1000);
    }
}