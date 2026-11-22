namespace ErpDashboard.Api.Features.Purchasing.PurchaseOrders;

public static class PurchaseOrderStatuses
{
    public static readonly StatusMachine<PurchaseOrderStatus> Machine = new("purchase order",
        new Dictionary<PurchaseOrderStatus, PurchaseOrderStatus[]>
        {
            [PurchaseOrderStatus.Pending] = [PurchaseOrderStatus.Received, PurchaseOrderStatus.Cancelled],
            [PurchaseOrderStatus.Received] = [],
            [PurchaseOrderStatus.Cancelled] = []
        });
}