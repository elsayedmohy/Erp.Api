namespace ErpDashboard.Api.Data.Enums;

public enum OrderStatus
{
    Draft = 0,
    Confirmed = 1,
    Shipped = 2,
    Delivered = 3,
    PendingApproval = 4,
    Cancelled = 5,
    Preparing = 6
}