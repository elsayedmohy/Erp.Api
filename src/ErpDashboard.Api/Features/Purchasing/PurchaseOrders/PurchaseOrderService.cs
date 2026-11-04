namespace ErpDashboard.Api.Features.Purchasing.PurchaseOrders;

public class PurchaseOrderService(AppDbContext dbContext) : IPurchaseOrderService
{
    private static readonly SortMap<PurchaseOrder> Sorts = new SortMap<PurchaseOrder>()
        .Add("orderDate", o => o.OrderDate)
        .Add("expectedDelivery", o => o.ExpectedDelivery)
        .Add("totalAmount", o => o.TotalAmount)
        .Add("status", o => o.Status)
        .Default("orderDate", descending: true)
        .TieBreaker(o => o.Id);

    private static readonly Dictionary<PurchaseOrderStatus, PurchaseOrderStatus[]> AllowedTransitions = new()
    {
        [PurchaseOrderStatus.Pending] = [PurchaseOrderStatus.Received, PurchaseOrderStatus.Cancelled],
    };

    public Task<PagedResult<PurchaseOrderResponse>> ListAsync(PurchaseOrderQuery query, CancellationToken ct)
    {
        if (query.From > query.To)
            throw new BadRequestException("'from' must be before or equal to 'to'.");

        var q = dbContext.PurchaseOrders.AsNoTracking();

        if (query.Status is { } status) q = q.Where(o => o.Status == status);
        if (query.SupplierId is { } sid) q = q.Where(o => o.SupplierId == sid);
        if (query.From is { } from) q = q.Where(o => o.OrderDate >= from);
        if (query.To is { } to) q = q.Where(o => o.OrderDate <= to);

        return Sorts.Apply(q, query.SortBy, query.SortDir)
            .Select(o => new PurchaseOrderResponse(
                o.Id,
                o.SupplierId,
                o.OrderDate,
                o.ExpectedDelivery,
                o.Status,
                o.TotalAmount
            ))
            .ToPagedResultAsync(query, ct);
    }

    public async Task<PurchaseOrderDetailsResponse> GetByIdAsync(Guid id, CancellationToken ct)
    {
        PurchaseOrder? order = await dbContext.PurchaseOrders.AsNoTracking()
            .Include(o => o.PurchaseLines)
            .FirstOrDefaultAsync(o => o.Id == id, ct);
        if (order is null)
            throw new NotFoundException("Purchase Order", id);

        return ToDetailedResponse(order);
    }


    public async Task<PurchaseOrderDetailsResponse> CreateAsync(CreatePurchaseOrderRequest request,
        CancellationToken ct)
    {
        if (!await dbContext.Suppliers.AnyAsync(c => c.Id == request.SupplierId, ct))
            throw new NotFoundException("Supplier ", request.SupplierId);


        var (lines, total) = await BuildLinesAsync(request.Lines, ct);

        var orderDate = request.OrderDate ?? DateOnly.FromDateTime(DateTime.UtcNow);

        if (request.ExpectedDelivery < orderDate)
            throw new BadRequestException("Expected delivery cannot be before the order date.");

        var order = new PurchaseOrder()
        {
            SupplierId = request.SupplierId,
            OrderDate = orderDate,
            ExpectedDelivery = request.ExpectedDelivery,
            Status = PurchaseOrderStatus.Pending,
            TotalAmount = total,
            PurchaseLines = lines
        };

        dbContext.PurchaseOrders.Add(order);
        await dbContext.SaveChangesAsync(ct);
        return ToDetailedResponse(order);
    }

    public async Task<PurchaseOrderDetailsResponse> UpdateAsync(Guid id, UpdatePurchaseOrderRequest request,
        CancellationToken ct)
    {
        var po = await dbContext.PurchaseOrders
                     .Include(p => p.PurchaseLines)
                     .FirstOrDefaultAsync(p => p.Id == id, ct)
                 ?? throw new NotFoundException("Purchase Order", id);

        if (po.Status != PurchaseOrderStatus.Pending)
            throw new BadRequestException("Only pending purchase orders can be updated.");

        if (request.ExpectedDelivery < po.OrderDate)
            throw new BadRequestException("Expected delivery cannot be before the order date.");

        var (lines, total) = await BuildLinesAsync(request.Lines, ct);

        po.ExpectedDelivery = request.ExpectedDelivery;
        po.PurchaseLines.Clear();
        foreach (var line in lines)
            po.PurchaseLines.Add(line);
        po.TotalAmount = total;

        await dbContext.SaveChangesAsync(ct);
        return ToDetailedResponse(po);
    }

    public async Task<PurchaseOrderResponse> UpdateStatusAsync(Guid id, UpdatePurchaseOrderStatusRequest request,
        CancellationToken ct)
    {
        PurchaseOrder? po = await dbContext.PurchaseOrders
            .Include(p => p.PurchaseLines)
            .FirstOrDefaultAsync(o => o.Id == id, ct);
        if (po is null)
            throw new NotFoundException("Purchase Order", id);

        if (!AllowedTransitions.TryGetValue(po.Status, out var allowed) || !allowed.Contains(request.Status))
            throw new BadRequestException(
                $"Cannot change purchase order status from '{po.Status}' to '{request.Status}'.");

        if (request.Status == PurchaseOrderStatus.Received)
        {
            var ids = po.PurchaseLines.Select(l => l.ProductId).Distinct().ToList();
            var products = await dbContext.Products
                .IgnoreQueryFilters()
                .Where(p => ids.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, ct);

            foreach (var line in po.PurchaseLines)
                products[line.ProductId].StockQuantity += line.Quantity;
        }

        po.Status = request.Status;
        await dbContext.SaveChangesAsync(ct);
        return ToResponse(po);
    }


    private static PurchaseOrderDetailsResponse ToDetailedResponse(PurchaseOrder o) =>
        new PurchaseOrderDetailsResponse(
            o.Id,
            o.SupplierId,
            o.OrderDate,
            o.ExpectedDelivery,
            o.Status,
            o.TotalAmount,
            o.PurchaseLines.Select(l => new PurchaseLineResponse(
                l.Id,
                l.ProductId,
                l.Quantity,
                l.UnitCost
            )).ToList());

    private static PurchaseOrderResponse ToResponse(PurchaseOrder o) =>
        new PurchaseOrderResponse(
            o.Id,
            o.SupplierId,
            o.OrderDate,
            o.ExpectedDelivery,
            o.Status,
            o.TotalAmount);

    private async Task<(List<PurchaseLine> Lines, decimal Total)> BuildLinesAsync(
        List<PurchaseLineRequest> requestLines, CancellationToken ct)
    {
        var duplicates = requestLines.GroupBy(l => l.ProductId)
            .Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (duplicates.Count > 0)
            throw new BadRequestException(
                $"Each product can appear only once. Duplicated: {string.Join(", ", duplicates)}");

        var ids = requestLines.Select(l => l.ProductId).ToList();
        var existing = await dbContext.Products
            .Where(p => ids.Contains(p.Id))
            .Select(p => p.Id)
            .ToListAsync(ct);

        var missing = ids.Except(existing).ToList();
        if (missing.Count > 0)
            throw new BadRequestException($"Products not found: {string.Join(", ", missing)}");

        var lines = requestLines.Select(l => new PurchaseLine
        {
            ProductId = l.ProductId,
            Quantity = l.Quantity,
            UnitCost = l.UnitCost
        }).ToList();

        return (lines, lines.Sum(l => l.Quantity * l.UnitCost));
    }
}