using ErpDashboard.Api.Features.Finance.Invoices;
using ErpDashboard.Api.Features.Inventory.Stock;

namespace ErpDashboard.Api.Features.Sales.Orders;

public class OrderService(AppDbContext dbContext,
    IStockService stock, 
    IOptions<SalesSettings> salesOptions,
    IWarehouseScope scope) : IOrderService
{

    
    private static readonly SortMap<Order> Sorts = new SortMap<Order>()
        .Add("orderDate", o => o.OrderDate)
        .Add("totalAmount", o => o.TotalAmount)
        .Add("status", o => o.Status)
        .Default("orderDate", descending: true)
        .TieBreaker(o => o.Id);

    private static readonly Dictionary<OrderStatus, OrderStatus> NextStatus = new()
    {
        [OrderStatus.Pending] = OrderStatus.Confirmed,
        [OrderStatus.Confirmed] = OrderStatus.Shipped,
        [OrderStatus.Shipped] = OrderStatus.Delivered,
    };

    public Task<PagedResult<OrderResponse>> ListAsync(OrderQuery query, CancellationToken ct)
    {
        if (query.From > query.To)
            throw new RequestValidationException("from", "'from' must be before or equal to 'to'.");

        var q = dbContext.Orders.Include(o => o.Showroom).AsNoTracking();

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
                o.ShowroomId,
                o.WarehouseId,
                o.Showroom.Name,
                o.OrderDate,
                o.Status,
                o.PaymentMethod,
                o.ShippingAddress,
                o.TotalAmount
            ))
            .ToPagedResultAsync(query, ct);
    }

    public async Task<OrderDetailsResponse> GetByIdAsync(Guid id, CancellationToken ct)
    {
        Order? order = await dbContext.Orders.AsNoTracking()
            .Include(o => o.Showroom)
            .Include(o => o.Invoice)
            .Include(o => o.OrderLines)
            .FirstOrDefaultAsync(o => o.Id == id, ct);
        if (order is null)
            throw new NotFoundException("Order", id);

        return ToDetailedResponse(order);
    }

    private const int InvoiceDueDays = 30;

    public async Task<OrderDetailsResponse> CreateAsync(CreateOrderRequest request, Guid createdById,CancellationToken ct)
    {
        var threshold = salesOptions.Value.DiscountApprovalThresholdPercent;
        var requiresApproval = false;
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
                       ?? throw new RequestValidationException("showroomId", $"Showroom '{showroomId}' does not exist.");

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
        
        var orderDate =  DateOnly.FromDateTime(DateTime.UtcNow);
        var orderLines = new List<OrderLine>();
        var invoiceLines = new List<InvoiceLineInput>();
        var insufficient = new List<StockShortage>();
        decimal total = 0;
        decimal taxRate = 14;
        var orderId = Guid.NewGuid();
        foreach (var line in request.Lines.OrderBy(l => l.ProductId))
        {
            var product = products[line.ProductId];
            var gross = line.Quantity * product.UnitPrice;
            
            if (line.Discount * 100 > gross * threshold)
                requiresApproval = true;
            
            if (line.Discount > gross)
                throw new RequestValidationException("lines",
                    $"Discount for '{product.Name}' ({line.Discount}) exceeds the line value ({gross}).");

            if (!await stock.TryApplyAsync(new StockChange(
                    warehouseId, product.Id, -line.Quantity, StockMovementType.SalesIssue, createdById, orderId), ct))
            {
                var available = await dbContext.WarehouseStocks
                    .Where(s => s.WarehouseId == warehouseId && s.ProductId == product.Id)
                    .Select(s => (int?)s.Quantity)
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

            invoiceLines.Add(new InvoiceLineInput(product.Id, product.Name, line.Quantity, product.UnitPrice, taxRate));
            if (line.Discount > 0)
                invoiceLines.Add(new InvoiceLineInput(product.Id, $"Discount - {product.Name}", 1, -line.Discount,
                    taxRate));
        }

        if (insufficient.Count > 0)
            throw new InsufficientStockException(warehouseId, insufficient);

        var invoice = InvoiceFactory.CreateDraft(
            showroomId, orderDate, orderDate.AddDays(InvoiceDueDays), invoiceLines);

        var order = new Order
        {
            Id = orderId,
            ShowroomId = showroomId,
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
            WarehouseId = warehouseId,
            Showroom = showroom,
            OrderDate = orderDate,
            Status = requiresApproval ? OrderStatus.PendingApproval : OrderStatus.Pending,
            CreatedById = createdById,
            PaymentMethod = request.PaymentMethod,
            ShippingAddress = request.ShippingAddress.Trim(),
            TotalAmount = total,
            OrderLines = orderLines,
            Invoice = invoice
        };

        dbContext.Orders.Add(order);
        await dbContext.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return ToDetailedResponse(order);
    }

    public async Task<OrderResponse> UpdateAsync(Guid id, UpdateOrderRequest request, CancellationToken ct)
    {
        Order? order = await dbContext.Orders
            .Include(o => o.Showroom)
            .FirstOrDefaultAsync(o => o.Id == id, ct);
        if (order is null)
            throw new NotFoundException("Order", id);

        if (order.Status != OrderStatus.Pending)
            throw new ConflictException("Only pending orders can be updated.");

        order.ShippingAddress = request.ShippingAddress.Trim();
        order.PaymentMethod = request.PaymentMethod;
        order.Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        await dbContext.SaveChangesAsync(ct);
        return ToResponse(order);
    }

    public async Task<OrderResponse> UpdateStatusAsync(Guid id, UpdateOrderStatusRequest request, CancellationToken ct)
    {
        Order? order = await dbContext.Orders
            .Include(o => o.Showroom)
            .Include(o => o.Invoice)
            .FirstOrDefaultAsync(o => o.Id == id, ct);
        if (order is null)
            throw new NotFoundException("Order", id);

        if (!NextStatus.TryGetValue(order.Status, out var allowed) || allowed != request.Status)
        {
            throw new ConflictException(
                $"Cannot change order status from '{order.Status}' to '{request.Status}'.");
        }

        order.Status = request.Status;

        if (request.Status == OrderStatus.Confirmed && order.Invoice is { Status: InvoiceStatus.Draft } invoice)
            invoice.Status = InvoiceStatus.Issued;

        await dbContext.SaveChangesAsync(ct);
        return ToResponse(order);
    }
    
    public async Task<OrderResponse> ApproveAsync(Guid id, Guid reviewerId, CancellationToken ct)
{
    var order = await GetForReviewAsync(id, reviewerId, ct);

    await ClaimReviewAsync(id, OrderStatus.Pending, reviewerId, null, ct);

    order.Status = OrderStatus.Pending;
    return ToResponse(order);
}

public async Task<OrderResponse> RejectAsync(
    Guid id, RejectOrderRequest request,
    Guid reviewerId, CancellationToken ct)
{
    var order = await GetForReviewAsync(id, reviewerId, ct);

    await using var transaction = await dbContext.Database.BeginTransactionAsync(ct);

    await ClaimReviewAsync(id, OrderStatus.Rejected, reviewerId, request.Reason.Trim(), ct);


    foreach (var line in order.OrderLines)
        await stock.TryApplyAsync(new StockChange(
            order.WarehouseId, line.ProductId, line.Quantity, StockMovementType.SalesReversal,
            reviewerId, order.Id, "Order rejected"), ct);

    if (order.Invoice is { Status: InvoiceStatus.Draft } invoice)
        invoice.Status = InvoiceStatus.Cancelled;

    await dbContext.SaveChangesAsync(ct);
    await transaction.CommitAsync(ct);

    order.Status = OrderStatus.Rejected;
    return ToResponse(order);
}



    public async Task DeleteAsync(Guid id,Guid deletedById, CancellationToken ct)
    {
        var order = await dbContext.Orders
                        .Include(o => o.OrderLines)
                        .Include(o => o.Invoice).ThenInclude(i => i!.InvoiceLines)
                        .FirstOrDefaultAsync(o => o.Id == id, ct)
                    ?? throw new NotFoundException("Order", id);

        if (order.Status != OrderStatus.Pending)
            throw new ConflictException("Only pending orders can be deleted.");

        if (order.Invoice is { Status: not InvoiceStatus.Draft })
            throw new ConflictException("The order's invoice is no longer a draft.");

        await using var transaction = await dbContext.Database.BeginTransactionAsync(ct);
        
        foreach (var line in order.OrderLines)
            await stock.TryApplyAsync(new StockChange(
                order.WarehouseId, line.ProductId, line.Quantity, StockMovementType.SalesReversal,
                deletedById, order.Id, "Order deleted"), ct);

        if (order.Invoice is not null)
            dbContext.Invoices.Remove(order.Invoice);
        dbContext.Orders.Remove(order);

        await dbContext.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }
    
    private async Task<Order> GetForReviewAsync(Guid id, Guid reviewerId, CancellationToken ct)
    {
        var order = await dbContext.Orders
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

    private async Task ClaimReviewAsync(
        Guid id, OrderStatus next, Guid reviewerId, string? reason, CancellationToken ct)
    {
        Guid? reviewer = reviewerId;
        DateTime? reviewedAt = DateTime.UtcNow;

        var claimed = await dbContext.Orders
            .Where(o => o.Id == id && o.Status == OrderStatus.PendingApproval)
            .ExecuteUpdateAsync(s => s
                .SetProperty(o => o.Status, next)
                .SetProperty(o => o.ReviewedById, reviewer)
                .SetProperty(o => o.ReviewedAt, reviewedAt)
                .SetProperty(o => o.RejectionReason, reason), ct);

        if (claimed == 0)
            throw new ConflictException("The order was already reviewed.");
    }

    private static OrderDetailsResponse ToDetailedResponse(Order o) =>
        new(
            o.Id,
            o.ShowroomId,
            o.WarehouseId,
            o.Invoice.Id,
            o.Showroom.Name,
            o.OrderDate,
            o.Status,
            o.PaymentMethod,
            o.ShippingAddress,
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
            o.ShowroomId,
            o.WarehouseId,
            o.Showroom.Name,
            o.OrderDate,
            o.Status,
            o.PaymentMethod,
            o.ShippingAddress,
            o.TotalAmount);
}