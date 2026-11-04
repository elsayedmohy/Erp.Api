using static ErpDashboard.Api.Common.Authorization.PermissionCodes;

namespace ErpDashboard.Api.Data.Seed;

public static class RolePermissions
{
    public static List<string> Admin => [.. All];

    public static List<string> Manager => Sorted(
        Customers.Read, Customers.Write,
        Products.Read,
        Suppliers.Read,
        PurchaseOrders.Read, PurchaseOrders.Write, PurchaseOrders.UpdateStatus,
        Orders.Read, Orders.Write, Orders.UpdateStatus,
        Invoices.Read,
        Employees.Read);

    public static List<string> InventoryManager => Sorted(
        Products.Read, Products.Write, Products.AdjustStock,
        Suppliers.Read, Suppliers.Write,
        PurchaseOrders.Read, PurchaseOrders.Write, PurchaseOrders.UpdateStatus);

    public static List<string> Accountant => Sorted(
        Customers.Read,
        Orders.Read,
        Invoices.Read, Invoices.Write, Invoices.Pay,
        Employees.Read, Employees.ViewSalary);

    public static List<string> User => Sorted(
        Customers.Read, Products.Read, Orders.Read);

    private static List<string> Sorted(params string[] codes) =>
        codes.Order(StringComparer.Ordinal).ToList();
}