namespace ErpDashboard.Api.Data.Configurations;

public class OrderLineConfiguration : IEntityTypeConfiguration<OrderLine>
{
    public void Configure(EntityTypeBuilder<OrderLine> b)
    {
        b.HasOne(l => l.Product)
            .WithMany()
            .HasForeignKey(l => l.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
        b.Property(l => l.ProductName).HasMaxLength(200).IsRequired();
        b.Property(l => l.Sku).HasMaxLength(50).IsRequired();
    }
}