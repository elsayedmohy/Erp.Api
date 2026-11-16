namespace ErpDashboard.Api.Features.Shipping.Shipments;

public sealed record BookVehicleRequest(
    [Required] Guid? CarrierId,
    [MaxLength(20)] string? PlateNumber = null,
    [MaxLength(20)] string? DriverPhone = null);

public sealed record BookShipmentRequest(
    [Required, MinLength(1), MaxLength(10)] List<BookVehicleRequest> Vehicles);

public sealed record ShipmentVehicleResponse(
    Guid CarrierId, string CarrierName, decimal Price, string? PlateNumber, string? DriverPhone);

public sealed record ShipmentResponse(
    Guid Id, Guid OrderId, string TrackingNumber, ShipmentStatus Status, Governorate Governorate,
    decimal TotalCost, DateTime BookedAt, Guid? BookedById, DateTime? CancelledAt,
    List<ShipmentVehicleResponse> Vehicles);

public sealed record AwaitingShipmentResponse(
    Guid OrderId, string ShowroomName, OrderStatus Status, DateOnly OrderDate,
    Governorate? ShippingGovernorate, string ShippingAddress);