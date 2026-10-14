using ErpDashboard.Api.Features.Finance.Invoices;

namespace ErpDashboard.Api.Features.Sales.Orders;

public class OrderService(AppDbContext dbContext) : IOrderService
{
    private static readonly SortMap<Order> Sorts = new SortMap<Order>()
        .Add("orderDate", o => o.OrderDate)
        .Add("totalAmount", o => o.TotalAmount)
        .Add("status", o => o.Status)
        .Default("orderDate", descending: true)
        .TieBreaker(o => o.Id);

    private static readonly Dictionary<OrderStatus, OrderStatus> NextStatus = new()
    {
        [OrderStatus.Pending]   = OrderStatus.Confirmed,
        [OrderStatus.Confirmed] = OrderStatus.Shipped,
        [OrderStatus.Shipped]   = OrderStatus.Delivered,
    };
    
    public Task<PagedResult<OrderResponse>> ListAsync(OrderQuery query, CancellationToken ct)
    {
        if (query.From > query.To)
            throw new BadRequestException("'from' must be before or equal to 'to'.");

        var q = dbContext.Orders.AsNoTracking();

        if (query.Status is { } status) q = q.Where(o => o.Status == status);
        if (query.CustomerId is { } cid) q = q.Where(o => o.CustomerId == cid);
        if (query.From is { } from) q = q.Where(o => o.OrderDate >= from);
        if (query.To is { } to) q = q.Where(o => o.OrderDate <= to);

        return Sorts.Apply(q, query.SortBy, query.SortDir)
            .Select(o => new OrderResponse(
                o.Id,
                o.CustomerId,
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
            .Include(o => o.OrderLines)
            .FirstOrDefaultAsync(o => o.Id == id, ct);
        if (order is null)
            throw new NotFoundException("Order", id);

        return ToDetailedResponse(order);
    }

    private const int InvoiceDueDays = 30;

    public async Task<OrderDetailsResponse> CreateAsync(CreateOrderRequest request, CancellationToken ct)
    {
        var duplicates = request.Lines
            .GroupBy(l => l.ProductId)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        if (duplicates.Count > 0)
            throw new BadRequestException(
                $"Each product can appear only once. Duplicated: {string.Join(", ", duplicates)}");

        if (!await dbContext.Customers.AnyAsync(c => c.Id == request.CustomerId, ct))
            throw new BadRequestException($"Customer '{request.CustomerId}' does not exist.");

        var ids = request.Lines.Select(l => l.ProductId).ToList();
        var products = await dbContext.Products
            .Where(p => ids.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, ct);

        var missing = ids.Except(products.Keys).ToList();
        if (missing.Count > 0)
            throw new BadRequestException($"Products not found: {string.Join(", ", missing)}");

        var orderDate = request.OrderDate ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var orderLines = new List<OrderLine>();
        var invoiceLines = new List<InvoiceLineInput>();
        var insufficient = new List<string>();
        decimal total = 0;
        decimal taxRate = 14;

        foreach (var line in request.Lines)
        {
            var product = products[line.ProductId];
            var gross = line.Quantity * product.UnitPrice;

            if (line.Discount > gross)
                throw new BadRequestException(
                    $"Discount for '{product.Name}' ({line.Discount}) exceeds the line value ({gross}).");

            if (product.StockQuantity < line.Quantity)
            {
                insufficient.Add($"{product.Name} (requested {line.Quantity}, available {product.StockQuantity})");
                continue;
            }

            product.StockQuantity -= line.Quantity;
            total += gross - line.Discount;

            orderLines.Add(new OrderLine
            {
                ProductId = product.Id,
                Quantity = line.Quantity,
                UnitPrice = product.UnitPrice,
                Discount = line.Discount
            });

            invoiceLines.Add(new InvoiceLineInput(product.Id, product.Name, line.Quantity, product.UnitPrice, taxRate));
            if (line.Discount > 0)
                invoiceLines.Add(new InvoiceLineInput(product.Id, $"Discount - {product.Name}", 1, -line.Discount, taxRate));

        }

        if (insufficient.Count > 0)
            throw new BadRequestException("Insufficient stock: " + string.Join("; ", insufficient));

        var invoice = InvoiceFactory.CreateDraft(
            request.CustomerId, orderDate, orderDate.AddDays(InvoiceDueDays), invoiceLines);

        var order = new Order
        {
            CustomerId = request.CustomerId,
            OrderDate = orderDate,
            Status = OrderStatus.Pending,
            PaymentMethod = request.PaymentMethod,
            ShippingAddress = request.ShippingAddress.Trim(),
            TotalAmount = total,
            OrderLines = orderLines,
            Invoice = invoice
        };

        dbContext.Orders.Add(order);
        await dbContext.SaveChangesAsync(ct);
        return ToDetailedResponse(order);
    }

    public async Task<OrderResponse> UpdateAsync(Guid id, UpdateOrderRequest request, CancellationToken ct)
    {
        Order? order = await dbContext.Orders.FirstOrDefaultAsync(o => o.Id == id, ct);
        if (order is null)
            throw new NotFoundException("Order", id);
        
        if (order.Status != OrderStatus.Pending)
            throw new BadRequestException("Only pending orders can be updated.");

        order.ShippingAddress = request.ShippingAddress.Trim();
        order.PaymentMethod = request.PaymentMethod;
        await dbContext.SaveChangesAsync(ct);
        return ToResponse(order);
    }

    public async Task<OrderResponse> UpdateStatusAsync(Guid id, UpdateOrderStatusRequest request, CancellationToken ct)
    {
        
        Order? order = await dbContext.Orders.AsNoTracking()
            .Include(o => o.Invoice)
            .FirstOrDefaultAsync(o => o.Id == id, ct);
        if (order is null)
            throw new NotFoundException("Order", id);
        
        if (!NextStatus.TryGetValue(order.Status, out var allowed) || allowed != request.Status)
        {
            throw new BadRequestException(
                $"Cannot change order status from '{order.Status}' to '{request.Status}'.");
        }
        
        order.Status = request.Status;

        if (request.Status == OrderStatus.Confirmed && order.Invoice is { Status: InvoiceStatus.Draft } invoice)
            invoice.Status = InvoiceStatus.Issued;

        await dbContext.SaveChangesAsync(ct);
        return ToResponse(order);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var order = await dbContext.Orders
                        .Include(o => o.OrderLines)
                        .Include(o => o.Invoice).ThenInclude(i => i!.InvoiceLines)
                        .FirstOrDefaultAsync(o => o.Id == id, ct)
                    ?? throw new NotFoundException("Order", id);

        if (order.Status != OrderStatus.Pending)
            throw new BadRequestException("Only pending orders can be deleted.");

        if (order.Invoice is { Status: not InvoiceStatus.Draft })
            throw new BadRequestException("The order's invoice is no longer a draft.");

        var ids = order.OrderLines.Select(l => l.ProductId).ToList();
        var products = await dbContext.Products
            .IgnoreQueryFilters()
            .Where(p => ids.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, ct);

        foreach (var line in order.OrderLines)
            products[line.ProductId].StockQuantity += line.Quantity;

        if (order.Invoice is not null)
            dbContext.Invoices.Remove(order.Invoice);
        dbContext.Orders.Remove(order);

        await dbContext.SaveChangesAsync(ct);
    }

    private static OrderDetailsResponse ToDetailedResponse(Order o) =>
        new OrderDetailsResponse(
            o.Id,
            o.CustomerId,
            o.OrderDate,
            o.Status,
            o.PaymentMethod,
            o.ShippingAddress,
            o.TotalAmount,
            o.OrderLines.Select(l => new OrderLineResponse(
                l.Id,
                l.ProductId,
                l.Quantity,
                l.UnitPrice,
                l.Discount)).ToList());

    private static OrderResponse ToResponse(Order o) =>
        new OrderResponse(
            o.Id,
            o.CustomerId,
            o.OrderDate,
            o.Status,
            o.PaymentMethod,
            o.ShippingAddress,
            o.TotalAmount);
}