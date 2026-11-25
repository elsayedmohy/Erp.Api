namespace ErpDashboard.Api.Features.Sales.Orders;

public interface IOrderPlacement
{
    Task<OrderDetailsResponse> CreateAsync(CreateOrderRequest request, Guid createdById, CancellationToken ct);
    Task<OrderResponse> UpdateAsync(Guid id, UpdateOrderRequest request, uint expectedVersion, CancellationToken ct);
}

public sealed class OrderPlacement(
    AppDbContext dbContext,
    IBusinessClock clock,
    IStockService stock,
    IWarehouseScope scope) : IOrderPlacement
{
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

        var orderLines = new List<OrderLine>();
        var insufficient = new List<StockShortage>();
        decimal total = 0;

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
            Id = Guid.NewGuid(),
            ShowroomId = showroomId,
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
            WarehouseId = warehouseId,
            Showroom = showroom,
            OrderDate = clock.Today,
            Status = OrderStatus.Draft,
            CreatedById = createdById,
            PaymentMethod = request.PaymentMethod,
            ShippingAddress = request.ShippingAddress.Trim(),
            ShippingGovernorate = request.ShippingGovernorate,
            TotalAmount = total,
            OrderLines = orderLines
        };

        dbContext.RecordStatusChange(order.Id, null, order.Status, createdById);
        dbContext.Orders.Add(order);
        await dbContext.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return order.ToDetailsResponse();
    }

    public async Task<OrderResponse> UpdateAsync(Guid id, UpdateOrderRequest request, uint expectedVersion,
        CancellationToken ct)
    {
        var order = await dbContext.LoadForChangeAsync(id, expectedVersion, ct);
        if (order.Status != OrderStatus.Draft)
            throw new ConflictException("Only draft orders can be updated.");

        dbContext.Entry(order).Property(o => o.Version).OriginalValue = expectedVersion;

        order.ShippingAddress = request.ShippingAddress.Trim();
        order.ShippingGovernorate = request.ShippingGovernorate;
        order.PaymentMethod = request.PaymentMethod;
        order.Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        await dbContext.SaveChangesAsync(ct);

        order.Version = await dbContext.CurrentVersionAsync(id, ct);
        return order.ToResponse();
    }
}
