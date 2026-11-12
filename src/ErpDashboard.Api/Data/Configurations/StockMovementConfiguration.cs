namespace ErpDashboard.Api.Data.Configurations;

public class StockMovementConfiguration : IEntityTypeConfiguration<StockMovement>
{
    public void Configure(EntityTypeBuilder<StockMovement> b)
    {
        b.Property(m => m.Type).HasConversion<string>().HasMaxLength(30);
        b.Property(m => m.Reason).HasMaxLength(500);
        b.HasIndex(m => new { m.WarehouseId, m.ProductId, m.CreatedAt });
        b.HasIndex(m => m.ReferenceId);
        b.ToTable(t => t.HasCheckConstraint("CK_StockMovements_Quantity_NonZero", "\"Quantity\" <> 0"));

        b.HasOne<Warehouse>().WithMany()
            .HasForeignKey(m => m.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Product>().WithMany()
            .HasForeignKey(m => m.ProductId).OnDelete(DeleteBehavior.Restrict);
    }
}