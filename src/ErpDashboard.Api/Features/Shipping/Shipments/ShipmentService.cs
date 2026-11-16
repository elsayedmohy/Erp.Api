using ErpDashboard.Api.Features.Finance.Invoices.DocumentNumber;

namespace ErpDashboard.Api.Features.Shipping.Shipments;

public sealed class ShipmentService(
    AppDbContext dbContext, IDocumentNumberGenerator numbers, IBusinessClock clock) : IShipmentService
{
    private static readonly OrderStatus[] BookableStatuses = [OrderStatus.Confirmed, OrderStatus.Preparing];

    public async Task<ShipmentResponse> BookAsync(
        Guid orderId, BookShipmentRequest request, Guid userId, CancellationToken ct)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(ct);

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""SELECT 1 FROM "Orders" WHERE "Id" = {orderId} FOR UPDATE""", ct);

        var order = await dbContext.Orders.AsNoTracking()
                        .Where(o => o.Id == orderId)
                        .Select(o => new { o.Status, o.ShippingGovernorate })
                        .SingleOrDefaultAsync(ct)
                    ?? throw new NotFoundException(nameof(Order), orderId);

        if (!BookableStatuses.Contains(order.Status))
            throw new ConflictException(
                $"Only confirmed or preparing orders can be booked. Current status: {order.Status}.",
                code: "not_bookable");
        if (order.ShippingGovernorate is not { } governorate)
            throw new ConflictException("Set the order's shipping governorate before booking.",
                code: "governorate_missing");
        if (await dbContext.Shipments.AnyAsync(s => s.OrderId == orderId, ct))
            throw new ConflictException("This order already has a shipment.", code: "already_booked");

        var carrierIds = request.Vehicles.Select(v => v.CarrierId!.Value).Distinct().ToList();
        var carriers = await dbContext.Carriers.AsNoTracking()
            .Where(c => carrierIds.Contains(c.Id))
            .Select(c => new
            {
                c.Id, c.Name, c.IsActive,
                Price = c.Rates.Where(r => r.Governorate == governorate)
                               .Select(r => (decimal?)r.VehiclePrice).FirstOrDefault()
            })
            .ToDictionaryAsync(c => c.Id, ct);

        var errors = new Dictionary<string, string[]>();
        for (var i = 0; i < request.Vehicles.Count; i++)
        {
            var id = request.Vehicles[i].CarrierId!.Value;
            if (!carriers.TryGetValue(id, out var c))
                errors[$"vehicles[{i}].carrierId"] = ["Carrier not found."];
            else if (!c.IsActive)
                errors[$"vehicles[{i}].carrierId"] = [$"{c.Name} is not active."];
            else if (c.Price is null)
                errors[$"vehicles[{i}].carrierId"] = [$"{c.Name} has no price for {governorate}."];
        }
        if (errors.Count > 0)
            throw new RequestValidationException(errors);

        var now = DateTime.UtcNow;
        var shipment = new Shipment
        {
            Id = Guid.NewGuid(),
            OrderId = orderId,
            TrackingNumber = await numbers.NextAsync("SHP", clock.Today.Year, ct),
            Status = ShipmentStatus.Booked,
            Governorate = governorate,
            BookedAt = now,
            BookedById = userId,
            Vehicles = request.Vehicles.Select(v =>
            {
                var c = carriers[v.CarrierId!.Value];
                return new ShipmentVehicle
                {
                    Id = Guid.NewGuid(),
                    CarrierId = c.Id,
                    CarrierName = c.Name,
                    Price = c.Price!.Value,
                    PlateNumber = Clean(v.PlateNumber),
                    DriverPhone = Clean(v.DriverPhone)
                };
            }).ToList()
        };
        shipment.TotalCost = shipment.Vehicles.Sum(v => v.Price);

        dbContext.Shipments.Add(shipment);
        await dbContext.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return ToResponse(shipment);
    }

    public async Task<ShipmentResponse> GetForOrderAsync(Guid orderId, CancellationToken ct)
    {
        if (!await dbContext.Orders.AnyAsync(o => o.Id == orderId, ct))
            throw new NotFoundException(nameof(Order), orderId);

        var shipment = await dbContext.Shipments.AsNoTracking()
                           .Include(s => s.Vehicles)
                           .SingleOrDefaultAsync(s => s.OrderId == orderId, ct)
                       ?? throw new NotFoundException(nameof(Shipment), orderId);
        return ToResponse(shipment);
    }

    public Task<List<AwaitingShipmentResponse>> AwaitingAsync(CancellationToken ct) =>
        dbContext.Orders.AsNoTracking()
            .Where(o => BookableStatuses.Contains(o.Status))
            .Where(o => !dbContext.Shipments.Any(s => s.OrderId == o.Id))
            .OrderBy(o => o.OrderDate).ThenBy(o => o.Id)
            .Select(o => new AwaitingShipmentResponse(
                o.Id, o.Showroom!.Name, o.Status, o.OrderDate, o.ShippingGovernorate, o.ShippingAddress))
            .Take(200)
            .ToListAsync(ct);

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static ShipmentResponse ToResponse(Shipment s) => new(
        s.Id, s.OrderId, s.TrackingNumber, s.Status, s.Governorate, s.TotalCost,
        s.BookedAt, s.BookedById, s.CancelledAt,
        s.Vehicles.Select(v => new ShipmentVehicleResponse(
            v.CarrierId, v.CarrierName, v.Price, v.PlateNumber, v.DriverPhone)).ToList());
}