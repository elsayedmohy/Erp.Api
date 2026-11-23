namespace ErpDashboard.Api.Data.Configurations;

public class OverdueReminderConfiguration : IEntityTypeConfiguration<OverdueReminder>
{
    public void Configure(EntityTypeBuilder<OverdueReminder> b)
    {
        b.Property(r => r.Id).ValueGeneratedNever();
        b.Property(r => r.Amount).HasPrecision(18, 2);

        // The database, not the code, guarantees one reminder per showroom per slot.
        b.HasIndex(r => new { r.ShowroomId, r.Date }).IsUnique();
        b.HasIndex(r => r.NotificationId).IsUnique();

        b.HasOne<Showroom>().WithMany().HasForeignKey(r => r.ShowroomId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Notification>().WithMany().HasForeignKey(r => r.NotificationId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class ReminderRunConfiguration : IEntityTypeConfiguration<ReminderRun>
{
    public void Configure(EntityTypeBuilder<ReminderRun> b)
    {
        b.HasKey(r => r.Date);
        b.Property(r => r.Date).ValueGeneratedNever();
    }
}
