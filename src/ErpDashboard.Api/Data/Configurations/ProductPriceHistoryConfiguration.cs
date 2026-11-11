namespace ErpDashboard.Api.Data.Configurations;

public class ProductPriceHistoryConfiguration : IEntityTypeConfiguration<ProductPriceHistory>
{
    public void Configure(EntityTypeBuilder<ProductPriceHistory> b)
    {
        b.ToTable("ProductPriceHistory");
        b.HasIndex(h => new { h.ProductId, h.ChangedAt });

        b.HasOne<Product>().WithMany()
            .HasForeignKey(h => h.ProductId).OnDelete(DeleteBehavior.Restrict);
    }
}