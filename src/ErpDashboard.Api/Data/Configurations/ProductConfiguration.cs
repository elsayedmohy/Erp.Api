namespace ErpDashboard.Api.Data.Configurations;

public class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> b)
    {
        b.Property(p => p.Name).HasMaxLength(200).IsRequired();
        b.Property(p => p.Sku).HasMaxLength(50).IsRequired();

        b.HasIndex(p => p.Sku).IsUnique().HasFilter("\"IsDeleted\" = false");
        b.ToTable(t => t.HasCheckConstraint("CK_Products_Sku_Normalized", "\"Sku\" = upper(btrim(\"Sku\"))"));
        b.Property(p => p.WeightKg).HasPrecision(8, 2);
        
        b.HasOne<Category>()
            .WithMany(c => c.Products)
            .HasForeignKey(p => p.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne<Material>().WithMany()
            .HasForeignKey(p => p.MaterialId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasQueryFilter(QueryFilters.SoftDelete, e => !e.IsDeleted);
    }
}