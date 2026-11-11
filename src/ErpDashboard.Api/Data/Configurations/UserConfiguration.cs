namespace ErpDashboard.Api.Data.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        b.HasOne<Showroom>().WithMany()
            .HasForeignKey(u => u.ShowroomId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}