namespace ErpDashboard.Api.Features.Shipping.Shipments;

public sealed record VehicleToBook(
    Guid CarrierId, string CarrierName, decimal Price, string? PlateNumber = null, string? DriverPhone = null);

public interface IShipmentBooking
{
    Task<Shipment> BookInTransactionAsync(
        Guid orderId, IReadOnlyList<VehicleToBook> vehicles, Guid? bookedById, CancellationToken ct);
}