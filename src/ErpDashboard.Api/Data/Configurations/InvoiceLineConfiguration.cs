namespace ErpDashboard.Api.Data.Configurations;

public class InvoiceLineConfiguration : IEntityTypeConfiguration<InvoiceLine>
{
    public void Configure(EntityTypeBuilder<InvoiceLine> b)
    {
        b.ToTable(t => t.HasCheckConstraint("CK_InvoiceLines_Discount", "\"Discount\" >= 0"));
    }
}