using ErpDashboard.Api.Features.Finance.Invoices.DocumentNumber;

namespace ErpDashboard.Api.Features.Shipping.Shipments;

public sealed class ShipmentService(
    AppDbContext dbContext,
    IDocumentNumberGenerator numbers,
    IBusinessClock clock,
    IVehiclePricer pricer) : IShipmentService, IShipmentBooking
{
    private static readonly OrderStatus[] BookableStatuses = [OrderStatus.Confirmed, OrderStatus.Preparing];

    public async Task<ShipmentResponse> BookAsync(
        Guid orderId, BookShipmentRequest request, Guid userId, CancellationToken ct)
    {

        var order = await dbContext.Orders.AsNoTracking()
                        .Where(o => o.Id == orderId)
                        .Select(o => new { o.ShippingGovernorate })
                        .SingleOrDefaultAsync(ct)
                    ?? throw new NotFoundException(nameof(Order), orderId);
        if (order.ShippingGovernorate is not { } governorate)
            throw new ConflictException("Set the order's shipping governorate before booking.",
                code: "governorate_missing");

        var priced = await pricer.PriceAsync(
            governorate, request.Vehicles.Select(v => v.CarrierId!.Value).ToList(), ct);
        var vehicles = request.Vehicles.Select((v, i) => new VehicleToBook(
            priced[i].CarrierId, priced[i].CarrierName, priced[i].Price,
            Clean(v.PlateNumber), Clean(v.DriverPhone))).ToList();

        await using var transaction = await dbContext.Database.BeginTransactionAsync(ct);
        var shipment = await BookInTransactionAsync(orderId, vehicles, userId, ct);
        await transaction.CommitAsync(ct);
        return ToResponse(shipment);
    }

    public async Task<Shipment> BookInTransactionAsync(
        Guid orderId, IReadOnlyList<VehicleToBook> vehicles, Guid? bookedById, CancellationToken ct)
    {
        // Serialises with cancel and with Shipped, which both update this row.
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""SELECT 1 FROM "Orders" WHERE "Id" = {orderId} FOR UPDATE""", ct);

        // Callers already decided visibility; background callers have no user.
        var order = await dbContext.Orders.IgnoreQueryFilters([QueryFilters.Showroom]).AsNoTracking()
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

        var shipment = new Shipment
        {
            Id = Guid.NewGuid(),
            OrderId = orderId,
            TrackingNumber = await numbers.NextAsync("SHP", clock.Today.Year, ct),
            Status = ShipmentStatus.Booked,
            Governorate = governorate,
            BookedAt = DateTime.UtcNow,
            BookedById = bookedById,
            Vehicles = vehicles.Select(v => new ShipmentVehicle
            {
                Id = Guid.NewGuid(),
                CarrierId = v.CarrierId,
                CarrierName = v.CarrierName,
                Price = v.Price,
                PlateNumber = v.PlateNumber,
                DriverPhone = v.DriverPhone
            }).ToList()
        };
        shipment.TotalCost = shipment.Vehicles.Sum(v => v.Price);

        dbContext.Shipments.Add(shipment);
        await dbContext.SaveChangesAsync(ct);
        return shipment;
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
        dbContext.Orders.IgnoreQueryFilters([QueryFilters.SoftDelete]).AsNoTracking()
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