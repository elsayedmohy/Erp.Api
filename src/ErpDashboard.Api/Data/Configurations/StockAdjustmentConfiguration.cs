namespace ErpDashboard.Api.Data.Configurations;

public class StockAdjustmentConfiguration : IEntityTypeConfiguration<StockAdjustment>
{
    public void Configure(EntityTypeBuilder<StockAdjustment> b)
    {
        b.Property(a => a.Reason).HasMaxLength(500).IsRequired();
        b.HasIndex(a => new { a.WarehouseId, a.ProductId });

        b.HasOne<Warehouse>().WithMany()
            .HasForeignKey(a => a.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Product>().WithMany()
            .HasForeignKey(a => a.ProductId).OnDelete(DeleteBehavior.Restrict);
    }
}