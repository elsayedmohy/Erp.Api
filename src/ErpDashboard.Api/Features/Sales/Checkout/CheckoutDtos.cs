namespace ErpDashboard.Api.Features.Sales.Checkout;

public sealed record CheckoutVehicleRequest([Required] Guid? CarrierId);

public sealed record StartCheckoutRequest(
    [Required, MinLength(1), MaxLength(10)] List<CheckoutVehicleRequest> Vehicles);

public sealed record CheckoutVehicleResponse(Guid CarrierId, string CarrierName, decimal Price);

public sealed record CheckoutResponse(
    Guid Id, Guid OrderId, CheckoutStatus Status,
    decimal GoodsAmount, decimal ShippingAmount, decimal TotalAmount,
    string? CheckoutUrl, DateTime ExpiresAt, string? CustomerMessage,
    List<CheckoutVehicleResponse> Vehicles);

public enum StartCheckoutOutcome { AwaitingPayment, AwaitingApproval }

public sealed record StartCheckoutResponse(StartCheckoutOutcome Outcome, uint OrderVersion, CheckoutResponse? Checkout);