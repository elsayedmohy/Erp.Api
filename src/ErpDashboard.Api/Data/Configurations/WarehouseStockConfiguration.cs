namespace ErpDashboard.Api.Data.Configurations;

public class WarehouseStockConfiguration : IEntityTypeConfiguration<WarehouseStock>
{
    public void Configure(EntityTypeBuilder<WarehouseStock> b)
    {

        b.HasKey(s => new { s.WarehouseId, s.ProductId });
        b.ToTable(t =>
        {
            t.HasCheckConstraint("CK_WarehouseStocks_Quantity", "\"Quantity\" >= 0");
            t.HasCheckConstraint("CK_WarehouseStocks_Reserved", "\"Reserved\" >= 0 AND \"Reserved\" <= \"Quantity\"");
        });
        
        b.HasOne(s => s.Warehouse).WithMany(w => w.Stock)
            .HasForeignKey(s => s.WarehouseId).OnDelete(DeleteBehavior.Restrict);

        b.HasOne(s => s.Product).WithMany()
            .HasForeignKey(s => s.ProductId).OnDelete(DeleteBehavior.Restrict);
    }
    
}