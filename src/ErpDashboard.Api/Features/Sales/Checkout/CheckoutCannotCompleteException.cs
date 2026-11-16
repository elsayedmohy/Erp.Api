namespace ErpDashboard.Api.Features.Sales.Checkout;

public sealed class CheckoutCannotCompleteException(string reason) : Exception(reason);