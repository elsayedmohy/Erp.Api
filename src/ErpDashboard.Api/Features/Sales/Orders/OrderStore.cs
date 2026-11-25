namespace ErpDashboard.Api.Features.Sales.Orders;

/// What OrderPlacement and OrderWorkflow both need: loading an order to change it, the version check,
/// and the status history row.
internal static class OrderStore
{
    /// Tracked, with Showroom, Invoice and OrderLines. 404 if missing; 409 order_changed if the version is old.
    public static async Task<Order> LoadForChangeAsync(
        this AppDbContext db, Guid id, uint expectedVersion, CancellationToken ct)
    {
        var order = await db.Orders.IgnoreQueryFilters([QueryFilters.SoftDelete])
                        .Include(o => o.Showroom)
                        .Include(o => o.Invoice)
                        .Include(o => o.OrderLines)
                        .FirstOrDefaultAsync(o => o.Id == id, ct)
                    ?? throw new NotFoundException("Order", id);

        if (order.Version != expectedVersion)
            throw Changed(order.Version);

        return order;
    }

    /// The version check failed, either on load or in the claim's WHERE.
    public static ConflictException Changed(uint? currentVersion = null) => new(
        "The order was changed by someone else. Refresh and try again.",
        code: "order_changed",
        extensions: currentVersion is { } v
            ? new Dictionary<string, object?> { ["currentVersion"] = v }
            : null);

    public static Task<uint> CurrentVersionAsync(this AppDbContext db, Guid id, CancellationToken ct) =>
        db.Orders.Where(o => o.Id == id).Select(o => o.Version).SingleAsync(ct);

    public static void RecordStatusChange(
        this AppDbContext db, Guid orderId, OrderStatus? from, OrderStatus to, Guid? userId, string? reason = null) =>
        db.OrderStatusChanges.Add(new OrderStatusChange
        {
            Id = Guid.NewGuid(),
            OrderId = orderId,
            FromStatus = from,
            ToStatus = to,
            ChangedById = userId,
            ChangedAt = DateTime.UtcNow,
            Reason = reason
        });
}
