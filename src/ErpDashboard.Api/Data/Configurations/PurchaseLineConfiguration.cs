namespace ErpDashboard.Api.Data.Configurations;

public class PurchaseLineConfiguration: IEntityTypeConfiguration<PurchaseLine>
{
    public void Configure(EntityTypeBuilder<PurchaseLine> b)
    {
            b.HasOne(l => l.Product)
                .WithMany()
                .HasForeignKey(l  => l.ProductId)
                .OnDelete(DeleteBehavior.Restrict);
            
            b.HasOne<PurchaseOrder>()
                .WithMany(o => o.PurchaseLines)
                .HasForeignKey(l => l.PurchaseOrderId)
                .OnDelete(DeleteBehavior.Cascade);
    }
}