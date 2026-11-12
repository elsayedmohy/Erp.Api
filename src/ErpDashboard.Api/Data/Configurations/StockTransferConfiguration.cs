namespace ErpDashboard.Api.Data.Configurations;

public class StockTransferConfiguration : IEntityTypeConfiguration<StockTransfer>
{
    public void Configure(EntityTypeBuilder<StockTransfer> b)
    {
        b.Property(t => t.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(t => t.Reason).HasMaxLength(500).IsRequired();
        b.Property(t => t.CancelReason).HasMaxLength(500);
        b.HasIndex(t => new { t.Status, t.ToWarehouseId });
        b.HasIndex(t => t.FromWarehouseId);
        b.ToTable(t => t.HasCheckConstraint("CK_StockTransfers_DifferentWarehouses",
            "\"FromWarehouseId\" <> \"ToWarehouseId\""));

        b.HasOne<Warehouse>().WithMany().HasForeignKey(t => t.FromWarehouseId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Warehouse>().WithMany().HasForeignKey(t => t.ToWarehouseId).OnDelete(DeleteBehavior.Restrict);

        b.HasMany(t => t.Lines).WithOne().HasForeignKey(l => l.TransferId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class StockTransferLineConfiguration : IEntityTypeConfiguration<StockTransferLine>
{
    public void Configure(EntityTypeBuilder<StockTransferLine> b)
    {
        b.HasIndex(l => new { l.TransferId, l.ProductId }).IsUnique();
        b.HasIndex(l => l.ProductId);
        b.ToTable(t => t.HasCheckConstraint("CK_StockTransferLines_Quantity", "\"Quantity\" > 0"));
        b.HasOne<Product>().WithMany().HasForeignKey(l => l.ProductId).OnDelete(DeleteBehavior.Restrict);
    }
}