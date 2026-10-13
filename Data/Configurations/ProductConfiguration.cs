namespace ErpDashboard.Api.Data.Configurations;

public class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> b)
    {
        b.Property(p => p.Name).HasMaxLength(200).IsRequired();
        b.Property(p => p.Sku).HasMaxLength(50).IsRequired();

        b.HasIndex(p => p.Sku).IsUnique().HasFilter("\"IsDeleted\" = false");

        b.HasOne<Category>()
            .WithMany(c => c.Products)
            .HasForeignKey(p => p.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasQueryFilter(p => !p.IsDeleted);
    }
}