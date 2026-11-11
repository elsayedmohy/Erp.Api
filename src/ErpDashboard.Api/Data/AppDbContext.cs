namespace ErpDashboard.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options, ICurrentShowroom currentShowroom) :
    IdentityUserContext<User, Guid>(options)
{
    private bool ShowroomAccessAll => currentShowroom.Current.Kind == ShowroomAccessKind.AccessAll;
    private Guid? ScopedShowroomId => currentShowroom.Current.ShowroomId;
    
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Showroom> Showrooms => Set<Showroom>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();
    public DbSet<PurchaseLine> PurchaseLines => Set<PurchaseLine>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderLine> OrderLines => Set<OrderLine>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<InvoiceLine> InvoiceLines => Set<InvoiceLine>();
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<Department> Departments => Set<Department>();
    
    public DbSet<Warehouse> Warehouses => Set<Warehouse>();
    public DbSet<WarehouseStock> WarehouseStocks => Set<WarehouseStock>();
    public DbSet<StockAdjustment> StockAdjustments => Set<StockAdjustment>();
    
    public DbSet<UserWarehouse> UserWarehouses => Set<UserWarehouse>();
    
    public DbSet<ProductPriceHistory> ProductPriceHistory => Set<ProductPriceHistory>();
    public DbSet<Material> Materials => Set<Material>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        modelBuilder.Entity<Order>().HasQueryFilter(QueryFilters.Showroom,
            o => ShowroomAccessAll || o.ShowroomId == ScopedShowroomId);
        modelBuilder.Entity<Invoice>().HasQueryFilter(QueryFilters.Showroom,
            i => ShowroomAccessAll || i.ShowroomId == ScopedShowroomId);
        modelBuilder.Entity<Showroom>().HasQueryFilter(QueryFilters.Showroom,
            s => ShowroomAccessAll || s.Id == ScopedShowroomId);
    }
    
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ApplySoftDelete();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }
    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken ct = default)
    {
        ApplySoftDelete();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, ct);
    }

    private void ApplySoftDelete()
    {
        foreach (var entry in ChangeTracker.Entries<ISoftDelete>()
                     .Where(e => e.State == EntityState.Deleted))
        {
            entry.State = EntityState.Modified;
            entry.Entity.IsDeleted = true;
            entry.Entity.DeletedAt = DateTime.UtcNow;
        }
    }
}