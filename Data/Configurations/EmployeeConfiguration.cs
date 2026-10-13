namespace ErpDashboard.Api.Data.Configurations;

public class EmployeeConfiguration : IEntityTypeConfiguration<Employee>
{
    public void Configure(EntityTypeBuilder<Employee> b)
    {
        b.HasIndex(e => e.Email).IsUnique().HasFilter("\"IsDeleted\" = false");

        b.HasOne<Department>()
            .WithMany(d => d.Employees)
            .HasForeignKey(e => e.DepartmentId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasQueryFilter(e => !e.IsDeleted);
    }
}