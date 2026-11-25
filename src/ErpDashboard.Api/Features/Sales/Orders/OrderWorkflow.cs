using Microsoft.EntityFrameworkCore.Query;

namespace ErpDashboard.Api.Features.Sales.Orders;

public interface IOrderWorkflow
{
    Task<OrderResponse> SubmitAsync(Guid id, uint expectedVersion, Guid userId, CancellationToken ct);
    Task<OrderResponse> ApproveAsync(Guid id, uint expectedVersion, Guid reviewerId, CancellationToken ct);
    Task<OrderResponse> RejectAsync(Guid id, RejectOrderRequest request, uint expectedVersion, Guid reviewerId, CancellationToken ct);
    Task<OrderResponse> UpdateStatusAsync(Guid id, UpdateOrderStatusRequest request, uint expectedVersion, Guid userId, CancellationToken ct);
    Task<OrderResponse> CancelAsync(Guid id, CancelOrderRequest request, uint expectedVersion, Guid userId, CancellationToken ct);
}

/// Every status change goes through MoveAsync: check the machine, claim the row in one UPDATE
/// (id + version + current status), do the side effects, write history, commit.
/// Each action only says where it goes and what else happens.
public sealed class OrderWorkflow(
    AppDbContext dbContext,
    IStockService stock,
    IOptions<SalesSettings> salesOptions) : IOrderWorkflow, IOrderCancellation
{
    /// The status endpoint only moves an order forward after it is confirmed; the rest have their own endpoints.
    private static readonly OrderStatus[] OperationalTargets =
        [OrderStatus.Preparing, OrderStatus.Shipped, OrderStatus.Delivered];

    public async Task<OrderResponse> SubmitAsync(Guid id, uint expectedVersion, Guid userId, CancellationToken ct)
    {
        var order = await dbContext.LoadForChangeAsync(id, expectedVersion, ct);
        var target = RequiresApproval(order.OrderLines) ? OrderStatus.PendingApproval : OrderStatus.Confirmed;

        return await MoveAsync(order, expectedVersion, target, userId, reason: null, ct);
    }

    public async Task<OrderResponse> ApproveAsync(Guid id, uint expectedVersion, Guid reviewerId, CancellationToken ct)
    {
        var order = await LoadForReviewAsync(id, expectedVersion, reviewerId, ct);

        return await MoveAsync(order, expectedVersion, OrderStatus.Confirmed, reviewerId, "Discount approved", ct,
            alsoSet: Reviewed(reviewerId, rejectionReason: null));
    }

    public async Task<OrderResponse> RejectAsync(
        Guid id, RejectOrderRequest request, uint expectedVersion, Guid reviewerId, CancellationToken ct)
    {
        var order = await LoadForReviewAsync(id, expectedVersion, reviewerId, ct);
        var reason = request.Reason.Trim();

        return await MoveAsync(order, expectedVersion, OrderStatus.Cancelled, reviewerId, $"Rejected: {reason}", ct,
            alsoSet: Reviewed(reviewerId, reason),
            sideEffects: () => UndoReservationAsync(order, ct));
    }

    public async Task<OrderResponse> UpdateStatusAsync(
        Guid id, UpdateOrderStatusRequest request, uint expectedVersion, Guid userId, CancellationToken ct)
    {
        var order = await dbContext.LoadForChangeAsync(id, expectedVersion, ct);
        if (!OperationalTargets.Contains(request.Status))
            throw new ConflictException(
                $"Use the dedicated endpoint to move an order to {request.Status}.",
                code: "invalid_transition");

        return await MoveAsync(order, expectedVersion, request.Status, userId, reason: null, ct,
            sideEffects: request.Status == OrderStatus.Shipped ? () => ShipAsync(order, userId, ct) : null);
    }

    public async Task<OrderResponse> CancelAsync(
        Guid id, CancelOrderRequest request, uint expectedVersion, Guid userId, CancellationToken ct)
    {
        var order = await dbContext.LoadForChangeAsync(id, expectedVersion, ct);

        return await MoveAsync(order, expectedVersion, OrderStatus.Cancelled, userId, request.Reason.Trim(), ct,
            sideEffects: () => UndoReservationAsync(order, ct));
    }

    /// Requires an open transaction (the caller's). No user and no version: it only wins if the status is
    /// still the one it read.
    public async Task<bool> CancelBySystemAsync(Guid orderId, string reason, CancellationToken ct)
    {
        // Background work has no user, so the showroom filter would hide every order.
        var order = await dbContext.Orders.IgnoreQueryFilters([QueryFilters.Showroom])
            .AsNoTracking()
            .Include(o => o.OrderLines)
            .SingleOrDefaultAsync(o => o.Id == orderId, ct);
        if (order is null || !OrderStatuses.Machine.CanMove(order.Status, OrderStatus.Cancelled))
            return false;

        var claimed = await dbContext.Orders.IgnoreQueryFilters([QueryFilters.Showroom])
            .Where(o => o.Id == orderId && o.Status == order.Status)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, OrderStatus.Cancelled), ct);
        if (claimed == 0)
            return false;

        await UndoReservationAsync(order, ct);
        dbContext.RecordStatusChange(order.Id, order.Status, OrderStatus.Cancelled, userId: null, reason);
        await dbContext.SaveChangesAsync(ct);
        return true;
    }

    // ---------- the one way to change status ----------

    private async Task<OrderResponse> MoveAsync(
        Order order, uint expectedVersion, OrderStatus to, Guid userId, string? reason, CancellationToken ct,
        Func<Task>? sideEffects = null,
        Action<UpdateSettersBuilder<Order>>? alsoSet = null)
    {
        var from = order.Status;
        OrderStatuses.Machine.EnsureCanMove(from, to);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(ct);

        // The WHERE is the lock: a second request with the same version, or one that moved the order
        // meanwhile, updates nothing and gets order_changed.
        var claimed = await dbContext.Orders
            .Where(o => o.Id == order.Id && o.Version == expectedVersion && o.Status == from)
            .ExecuteUpdateAsync(s =>
            {
                s.SetProperty(o => o.Status, to);
                alsoSet?.Invoke(s);
            }, ct);
        if (claimed == 0)
            throw OrderStore.Changed();

        // A side effect that throws rolls back the claim with it (the transaction is disposed uncommitted).
        if (sideEffects is not null)
            await sideEffects();

        dbContext.RecordStatusChange(order.Id, from, to, userId, reason);
        await dbContext.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        order.Version = await dbContext.CurrentVersionAsync(order.Id, ct);
        order.Status = to;
        return order.ToResponse();
    }

    // ---------- side effects ----------

    /// Every way to Cancelled (cancel, reject, system cancel) undoes the same things.
    private async Task UndoReservationAsync(Order order, CancellationToken ct)
    {
        await dbContext.Shipments
            .Where(s => s.OrderId == order.Id && s.Status == ShipmentStatus.Booked)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Status, ShipmentStatus.Cancelled)
                .SetProperty(x => x.CancelledAt, DateTime.UtcNow), ct);

        foreach (var line in order.OrderLines.OrderBy(l => l.ProductId))
            await stock.ReleaseAsync(order.WarehouseId, line.ProductId, line.Quantity, ct);
    }

    private async Task ShipAsync(Order order, Guid userId, CancellationToken ct)
    {
        if (!await dbContext.Shipments.AnyAsync(s => s.OrderId == order.Id && s.Status == ShipmentStatus.Booked, ct))
            throw new ConflictException("Book a shipment before marking the order as shipped.",
                code: "shipment_required");

        foreach (var line in order.OrderLines.OrderBy(l => l.ProductId))
            await stock.IssueReservedAsync(new StockChange(
                order.WarehouseId, line.ProductId, -line.Quantity,
                StockMovementType.SalesIssue, userId, order.Id), ct);
    }

    // ---------- review ----------

    private async Task<Order> LoadForReviewAsync(Guid id, uint expectedVersion, Guid reviewerId, CancellationToken ct)
    {
        var order = await dbContext.LoadForChangeAsync(id, expectedVersion, ct);

        if (order.Status != OrderStatus.PendingApproval)
            throw new ConflictException("Only orders pending approval can be reviewed.");

        if (order.CreatedById == reviewerId)
            throw new ForbiddenException("You cannot review an order you created.");

        return order;
    }

    private static Action<UpdateSettersBuilder<Order>> Reviewed(Guid reviewerId, string? rejectionReason)
    {
        Guid? reviewer = reviewerId;
        DateTime? reviewedAt = DateTime.UtcNow;
        return s => s
            .SetProperty(o => o.ReviewedById, reviewer)
            .SetProperty(o => o.ReviewedAt, reviewedAt)
            .SetProperty(o => o.RejectionReason, rejectionReason);
    }

    private bool RequiresApproval(IEnumerable<OrderLine> lines)
    {
        var threshold = salesOptions.Value.DiscountApprovalThresholdPercent;
        return lines.Any(l => l.Discount * 100 > l.Quantity * l.UnitPrice * threshold);
    }
}
