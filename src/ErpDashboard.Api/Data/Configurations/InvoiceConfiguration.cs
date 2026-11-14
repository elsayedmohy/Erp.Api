namespace ErpDashboard.Api.Data.Configurations;

public class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> b)
    {
        b.HasOne<Showroom>()
            .WithMany(c => c.Invoices)
            .HasForeignKey(i => i.ShowroomId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne<Order>()
            .WithOne(o => o.Invoice)
            .HasForeignKey<Invoice>(i => i.OrderId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasMany(i => i.InvoiceLines)
            .WithOne()
            .HasForeignKey(l => l.InvoiceId)
            .OnDelete(DeleteBehavior.Cascade);
        
        b.Property(i => i.Number).HasMaxLength(20);
        b.HasIndex(i => i.Number).IsUnique().HasFilter("\"Number\" IS NOT NULL");
        
        b.ToTable(t => t.HasCheckConstraint("CK_Invoices_IssuedHasNumber",
            "\"Status\" = 2 OR \"Number\" IS NOT NULL"));
    }
}