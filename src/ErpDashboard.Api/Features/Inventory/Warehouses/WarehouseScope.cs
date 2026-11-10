namespace ErpDashboard.Api.Features.Inventory.Warehouses;

public interface IWarehouseScope
{
    IQueryable<Guid> AccessibleWarehouseIds();
    Task<bool> CanAccessAsync(Guid warehouseId, CancellationToken ct);
    bool HasAccessAll { get; }
}

public sealed class WarehouseScope(
    AppDbContext db,
    ICurrentUserPermissions permissions,
    IHttpContextAccessor accessor) : IWarehouseScope
{
    public bool HasAccessAll =>
        accessor.HttpContext is null || permissions.Has(PermissionCodes.Warehouses.AccessAll);

    public IQueryable<Guid> AccessibleWarehouseIds()
    {
        if (HasAccessAll)
            return db.Warehouses.Select(w => w.Id);

        var user = accessor.HttpContext!.User;
        if (user.Identity?.IsAuthenticated != true)
            return db.Warehouses.Where(_ => false).Select(w => w.Id);

        var userId = user.GetUserId();
        return db.UserWarehouses.Where(x => x.UserId == userId).Select(x => x.WarehouseId);
    }

    public Task<bool> CanAccessAsync(Guid warehouseId, CancellationToken ct) =>
        AccessibleWarehouseIds().AnyAsync(id => id == warehouseId, ct);
}