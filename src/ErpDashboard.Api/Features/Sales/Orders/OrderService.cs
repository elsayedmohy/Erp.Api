namespace ErpDashboard.Api.Features.Sales.Orders;

public class OrderService(
    AppDbContext dbContext,
    IBusinessClock clock,
    IStockService stock,
    IOptions<SalesSettings> salesOptions,
    IWarehouseScope scope) : IOrderService, IOrderCancellation
{
    private static readonly SortMap<Order> Sorts = new SortMap<Order>()
        .Add("orderDate", o => o.OrderDate)
        .Add("totalAmount", o => o.TotalAmount)
        .Add("status", o => o.Status)
        .Default("orderDate", descending: true)
        .TieBreaker(o => o.Id);
    

    private static readonly OrderStatus[] OperationalTargets =
        [OrderStatus.Preparing, OrderStatus.Shipped, OrderStatus.Delivered];


    private async Task ClaimAsync(Guid id, uint expectedVersion, OrderStatus from, OrderStatus to, CancellationToken ct)
    {
        var claimed = await dbContext.Orders
            .Where(o => o.Id == id && o.Version == expectedVersion && o.Status == from)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, to), ct);

        if (claimed == 0)
            throw new ConflictException(
                "The order was changed by someone else. Refresh and try again.",
                code: "order_changed");
    }


    public Task<PagedResult<OrderResponse>> ListAsync(OrderQuery query, CancellationToken ct)
    {
        if (query.From > query.To)
            throw new RequestValidationException("from", "'from' must be before or equal to 'to'.");

        var q = dbContext.Orders.IgnoreQueryFilters([QueryFilters.SoftDelete]).Include(o => o.Showroom).AsNoTracking();

        if (query.GetSearchPattern() is { } p)
            q = q.Where(o =>
                EF.Functions.ILike(o.Id.ToString(), p, @"\")
            );

        if (query.Status is { } status) q = q.Where(o => o.Status == status);
        if (query.ShowroomId is { } cid) q = q.Where(o => o.ShowroomId == cid);
        if (query.From is { } from) q = q.Where(o => o.OrderDate >= from);
        if (query.To is { } to) q = q.Where(o => o.OrderDate <= to);

        return Sorts.Apply(q, query.SortBy, query.SortDir)
            .Select(o => new OrderResponse(
                o.Id,
                o.Version,
                o.ShowroomId,
                o.WarehouseId,
                o.Showroom.Name,
                o.OrderDate,
                o.Status,
                o.PaymentMethod,
                o.ShippingAddress,
                o.ShippingGovernorate,
                o.TotalAmount
            ))
            .ToPagedResultAsync(query, ct);
    }

    public async Task<OrderDetailsResponse> GetByIdAsync(Guid id, CancellationToken ct)
    {
        Order? order = await dbContext.Orders.IgnoreQueryFilters([QueryFilters.SoftDelete])
            .Include(o => o.Showroom)
            .Include(o => o.Invoice)
            .Include(o => o.OrderLines)
            .FirstOrDefaultAsync(o => o.Id == id, ct);
        if (order is null)
            throw new NotFoundException("Order", id);

        return ToDetailedResponse(order);
    }


    public async Task<OrderDetailsResponse> CreateAsync(CreateOrderRequest request, Guid createdById,
        CancellationToken ct)
    {
        var duplicates = request.Lines
            .GroupBy(l => l.ProductId)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        if (duplicates.Count > 0)
            throw new RequestValidationException("lines",
                $"Each product can appear only once. Duplicated: {string.Join(", ", duplicates)}");

        var showroomId = request.ShowroomId!.Value;
        var showroom = await dbContext.Showrooms.FirstOrDefaultAsync(c => c.Id == showroomId, ct)
                       ?? throw new RequestValidationException("showroomId",
                           $"Showroom '{showroomId}' does not exist.");

        var ids = request.Lines.Select(l => l.ProductId).ToList();
        var products = await dbContext.Products
            .Where(p => ids.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, ct);

        var missing = ids.Except(products.Keys).ToList();
        if (missing.Count > 0)
            throw new RequestValidationException("lines", $"Products not found: {string.Join(", ", missing)}");

        var warehouseId = request.WarehouseId!.Value;
        if (!await scope.CanAccessAsync(warehouseId, ct))
            throw new RequestValidationException("warehouseId", $"Warehouse '{warehouseId}' does not exist.");

        await using var transaction = await dbContext.Database.BeginTransactionAsync(ct);

        var orderDate = clock.Today;
        var orderLines = new List<OrderLine>();
        var insufficient = new List<StockShortage>();
        decimal total = 0;
        var orderId = Guid.NewGuid();
        foreach (var line in request.Lines.OrderBy(l => l.ProductId))
        {
            var product = products[line.ProductId];
            var gross = line.Quantity * product.UnitPrice;


            if (line.Discount > gross)
                throw new RequestValidationException("lines",
                    $"Discount for '{product.Name}' ({line.Discount}) exceeds the line value ({gross}).");

            if (!await stock.TryReserveAsync(warehouseId, product.Id, line.Quantity, ct))
            {
                var available = await dbContext.WarehouseStocks
                    .Where(s => s.WarehouseId == warehouseId && s.ProductId == product.Id)
                    .Select(s => (int?)(s.Quantity - s.Reserved))
                    .SingleOrDefaultAsync(ct) ?? 0;

                insufficient.Add(new StockShortage(product.Id, product.Name, line.Quantity, available));
                continue;
            }

            total += gross - line.Discount;

            orderLines.Add(new OrderLine
            {
                ProductId = product.Id,
                ProductName = product.Name,
                Sku = product.Sku,
                Quantity = line.Quantity,
                UnitPrice = product.UnitPrice,
                Discount = line.Discount
            });
        }

        if (insufficient.Count > 0)
            throw new InsufficientStockException(warehouseId, insufficient);

        var order = new Order
        {
            Id = orderId,
            ShowroomId = showroomId,
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
            WarehouseId = warehouseId,
            Showroom = showroom,
            OrderDate = orderDate,
            Status = OrderStatus.Draft,
            CreatedById = createdById,
            PaymentMethod = request.PaymentMethod,
            ShippingAddress = request.ShippingAddress.Trim(),
            ShippingGovernorate = request.ShippingGovernorate,
            TotalAmount = total,
            OrderLines = orderLines
        };

        RecordStatusChange(orderId, null, order.Status, createdById);
        dbContext.Orders.Add(order);
        await dbContext.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return ToDetailedResponse(order);
    }

    public async Task<OrderResponse> UpdateAsync(Guid id, UpdateOrderRequest request, uint expectedVersion,
        CancellationToken ct)
    {
        var order = await LoadAsync(id, ct);
        EnsureVersion(order, expectedVersion);
        if (order.Status != OrderStatus.Draft)
            throw new ConflictException("Only draft orders can be updated.");

        dbContext.Entry(order).Property(o => o.Version).OriginalValue = expectedVersion;

        order.ShippingAddress = request.ShippingAddress.Trim();
        order.ShippingGovernorate = request.ShippingGovernorate;
        order.PaymentMethod = request.PaymentMethod;
        order.Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        await dbContext.SaveChangesAsync(ct);
        order.Version = await CurrentVersionAsync(id, ct);
        return ToResponse(order);
    }

    public async Task<OrderResponse> UpdateStatusAsync(
        Guid id, UpdateOrderStatusRequest request, uint expectedVersion, Guid userId, CancellationToken ct)
    {
        var order = await LoadAsync(id, ct);
        EnsureVersion(order, expectedVersion);
        var current = order.Status;
        if (!OperationalTargets.Contains(request.Status))
            throw new ConflictException(
                $"Use the dedicated endpoint to move an order to {request.Status}.",
                code: "invalid_transition");
        OrderStatuses.Machine.EnsureCanMove(current, request.Status);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(ct);
        await ClaimAsync(id, expectedVersion, current, request.Status, ct);

        if (request.Status == OrderStatus.Shipped
            && !await dbContext.Shipments.AnyAsync(s => s.OrderId == id && s.Status == ShipmentStatus.Booked, ct))
            throw new ConflictException("Book a shipment before marking the order as shipped.",
                code: "shipment_required");

        if (request.Status == OrderStatus.Shipped)
            foreach (var line in order.OrderLines.OrderBy(l => l.ProductId))
                await stock.IssueReservedAsync(new StockChange(
                    order.WarehouseId, line.ProductId, -line.Quantity,
                    StockMovementType.SalesIssue, userId, order.Id), ct);

        RecordStatusChange(id, current, request.Status, userId);
        await dbContext.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        order.Version = await CurrentVersionAsync(id, ct);
        order.Status = request.Status;
        return ToResponse(order);
    }

    public async Task<OrderResponse> SubmitAsync(Guid id, uint expectedVersion, Guid userId, CancellationToken ct)
    {
        var order = await LoadAsync(id, ct);
        EnsureVersion(order, expectedVersion);
        var target = RequiresApproval(order.OrderLines) ? OrderStatus.PendingApproval : OrderStatus.Confirmed;
        OrderStatuses.Machine.EnsureCanMove(order.Status, target);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(ct);
        await ClaimAsync(id, expectedVersion, order.Status, target, ct);
        RecordStatusChange(id, order.Status, target, userId);
        await dbContext.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        order.Version = await CurrentVersionAsync(id, ct);
        order.Status = target;
        return ToResponse(order);
    }

    public async Task<OrderResponse> ApproveAsync(Guid id, uint expectedVersion, Guid reviewerId, CancellationToken ct)
    {
        var order = await GetForReviewAsync(id, reviewerId, ct);
        EnsureVersion(order, expectedVersion);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(ct);

        await ClaimReviewAsync(id, expectedVersion, OrderStatus.Confirmed, reviewerId, null, ct);
        RecordStatusChange(id, OrderStatus.PendingApproval, OrderStatus.Confirmed, reviewerId, "Discount approved");

        await dbContext.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        order.Version = await CurrentVersionAsync(id, ct);
        order.Status = OrderStatus.Confirmed;
        return ToResponse(order);
    }

    public async Task<OrderResponse> RejectAsync(
        Guid id, RejectOrderRequest request,
        uint expectedVersion,
        Guid reviewerId, CancellationToken ct)
    {
        var order = await GetForReviewAsync(id, reviewerId, ct);
        EnsureVersion(order, expectedVersion);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(ct);

        await ClaimReviewAsync(id, expectedVersion, OrderStatus.Cancelled, reviewerId, request.Reason.Trim(), ct);
        RecordStatusChange(id, OrderStatus.PendingApproval, OrderStatus.Cancelled, reviewerId,
            $"Rejected: {request.Reason.Trim()}");
        foreach (var line in order.OrderLines)
            await stock.ReleaseAsync(order.WarehouseId, line.ProductId, line.Quantity, ct);


        await dbContext.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        order.Version = await CurrentVersionAsync(id, ct);
        order.Status = OrderStatus.Cancelled;
        return ToResponse(order);
    }

    public async Task<IReadOnlyList<OrderStatusChangeResponse>> GetHistoryAsync(Guid id, CancellationToken ct)
    {
        if (!await dbContext.Orders.AnyAsync(o => o.Id == id, ct))
            throw new NotFoundException("Order", id);

        return await (
                from c in dbContext.OrderStatusChanges.AsNoTracking()
                where c.OrderId == id
                join u in dbContext.Users on c.ChangedById equals (Guid?)u.Id into users
                from u in users.DefaultIfEmpty()
                orderby c.ChangedAt, c.Id
                select new OrderStatusChangeResponse(
                    c.FromStatus,
                    c.ToStatus,
                    c.ChangedById,
                    u == null ? null : u.FirstName + " " + u.LastName,
                    c.ChangedAt,
                    c.Reason))
            .ToListAsync(ct);
    }


    public async Task<OrderResponse> CancelAsync(Guid id, CancelOrderRequest request, uint expectedVersion, Guid userId,
        CancellationToken ct)
    {
        var order = await LoadAsync(id, ct);
        EnsureVersion(order, expectedVersion);
        OrderStatuses.Machine.EnsureCanMove(order.Status, OrderStatus.Cancelled);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(ct);
        await ClaimAsync(id, expectedVersion, order.Status, OrderStatus.Cancelled, ct);
        await ApplyCancellationAsync(order, userId, request.Reason.Trim(), ct);
        await dbContext.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        order.Version = await CurrentVersionAsync(id, ct);
        order.Status = OrderStatus.Cancelled;
        return ToResponse(order);
    }

    public async Task<bool> CancelBySystemAsync(Guid orderId, string reason, CancellationToken ct)
    {
        // Background work has no user, so the showroom filter would hide every order.
        var order = await dbContext.Orders.IgnoreQueryFilters([QueryFilters.Showroom])
            .AsNoTracking()
            .Include(o => o.OrderLines)
            .SingleOrDefaultAsync(o => o.Id == orderId, ct);
        if (order is null || !OrderStatuses.Machine.CanMove(order.Status, OrderStatus.Cancelled))
            return false;

        // Same claim as a user cancel, minus the version: only wins if the status is still what we read.
        var claimed = await dbContext.Orders.IgnoreQueryFilters([QueryFilters.Showroom])
            .Where(o => o.Id == orderId && o.Status == order.Status)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, OrderStatus.Cancelled), ct);
        if (claimed == 0)
            return false;

        await ApplyCancellationAsync(order, null, reason, ct);
        await dbContext.SaveChangesAsync(ct);
        return true;
    }

    private async Task ApplyCancellationAsync(Order order, Guid? userId, string reason, CancellationToken ct)
    {
        await dbContext.Shipments
            .Where(s => s.OrderId == order.Id && s.Status == ShipmentStatus.Booked)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Status, ShipmentStatus.Cancelled)
                .SetProperty(x => x.CancelledAt, DateTime.UtcNow), ct);

        foreach (var line in order.OrderLines.OrderBy(l => l.ProductId))
            await stock.ReleaseAsync(order.WarehouseId, line.ProductId, line.Quantity, ct);

        RecordStatusChange(order.Id, order.Status, OrderStatus.Cancelled, userId, reason);
    }

    private async Task<Order> LoadAsync(Guid id, CancellationToken ct) =>
        await dbContext.Orders.IgnoreQueryFilters([QueryFilters.SoftDelete])
            .Include(o => o.Showroom)
            .Include(o => o.Invoice)
            .Include(o => o.OrderLines)
            .FirstOrDefaultAsync(o => o.Id == id, ct)
        ?? throw new NotFoundException("Order", id);

    private static void EnsureVersion(Order order, uint expected)
    {
        if (order.Version != expected)
            throw new ConflictException(
                "The order was changed by someone else. Refresh and try again.",
                code: "order_changed",
                extensions: new Dictionary<string, object?> { ["currentVersion"] = order.Version });
    }

    private async Task<Order> GetForReviewAsync(Guid id, Guid reviewerId, CancellationToken ct)
    {
        var order = await dbContext.Orders.IgnoreQueryFilters([QueryFilters.SoftDelete])
                        .Include(o => o.Showroom)
                        .Include(o => o.OrderLines)
                        .Include(o => o.Invoice)
                        .FirstOrDefaultAsync(o => o.Id == id, ct)
                    ?? throw new NotFoundException("Order", id);

        if (order.Status != OrderStatus.PendingApproval)
            throw new ConflictException("Only orders pending approval can be reviewed.");

        if (order.CreatedById == reviewerId)
            throw new ForbiddenException("You cannot review an order you created.");

        return order;
    }

    private bool RequiresApproval(IEnumerable<OrderLine> lines)
    {
        var threshold = salesOptions.Value.DiscountApprovalThresholdPercent;
        return lines.Any(l => l.Discount * 100 > l.Quantity * l.UnitPrice * threshold);
    }

    private async Task ClaimReviewAsync(
        Guid id, uint expectedVersion, OrderStatus next, Guid reviewerId, string? reason, CancellationToken ct)
    {
        Guid? reviewer = reviewerId;
        DateTime? reviewedAt = DateTime.UtcNow;

        var claimed = await dbContext.Orders
            .Where(o => o.Id == id && o.Status == OrderStatus.PendingApproval && o.Version == expectedVersion)
            .ExecuteUpdateAsync(s => s
                .SetProperty(o => o.Status, next)
                .SetProperty(o => o.ReviewedById, reviewer)
                .SetProperty(o => o.ReviewedAt, reviewedAt)
                .SetProperty(o => o.RejectionReason, reason), ct);

        if (claimed == 0)
            throw new ConflictException(
                "The order was changed by someone else. Refresh and try again.",
                code: "order_changed");
    }

    private void RecordStatusChange(Guid orderId, OrderStatus? from, OrderStatus to,
        Guid? userId, string? reason = null) =>
        dbContext.OrderStatusChanges.Add(new OrderStatusChange
        {
            Id = Guid.NewGuid(),
            OrderId = orderId,
            FromStatus = from,
            ToStatus = to,
            ChangedById = userId,
            ChangedAt = DateTime.UtcNow,
            Reason = reason
        });

    private static OrderDetailsResponse ToDetailedResponse(Order o) =>
        new(
            o.Id,
            o.Version,
            o.ShowroomId,
            o.WarehouseId,
            o.Invoice?.Id,
            o.Showroom.Name,
            o.OrderDate,
            o.Status,
            o.PaymentMethod,
            o.ShippingAddress,
            o.ShippingGovernorate,
            o.Notes,
            o.TotalAmount,
            o.OrderLines.Select(l => new OrderLineResponse(
                l.Id,
                l.ProductId,
                l.ProductName,
                l.Sku,
                l.Quantity,
                l.UnitPrice,
                l.Discount)).ToList());

    private static OrderResponse ToResponse(Order o) =>
        new OrderResponse(
            o.Id,
            o.Version,
            o.ShowroomId,
            o.WarehouseId,
            o.Showroom.Name,
            o.OrderDate,
            o.Status,
            o.PaymentMethod,
            o.ShippingAddress,
            o.ShippingGovernorate,
            o.TotalAmount);

    private Task<uint> CurrentVersionAsync(Guid id, CancellationToken ct) =>
        dbContext.Orders.Where(o => o.Id == id).Select(o => o.Version).SingleAsync(ct);
}