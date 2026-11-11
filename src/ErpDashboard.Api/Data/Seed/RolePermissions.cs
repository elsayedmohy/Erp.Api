using static ErpDashboard.Api.Common.Authorization.PermissionCodes;

namespace ErpDashboard.Api.Data.Seed;

public static class RolePermissions
{
    public static List<string> Admin => [.. All];

    public static List<string> Manager => Sorted(
        Showrooms.Read, Showrooms.Write,
        Products.Read,
        Categories.Read,
        Suppliers.Read,
        PurchaseOrders.Read, PurchaseOrders.Write, PurchaseOrders.UpdateStatus,
        Orders.Read, Orders.Write, Orders.UpdateStatus,
        Orders.ApproveDiscount,
        Warehouses.Read,
        Invoices.Read,
        Departments.Read,
        Employees.Read,
        Showrooms.AccessAll);

    public static List<string> InventoryManager => Sorted(
        Warehouses.Read,
        Warehouses.Write,
        Products.Read,
        Categories.Read,
        Categories.Write,
        Products.Write, Products.AdjustStock,
        Suppliers.Read, Suppliers.Write,
        PurchaseOrders.Read, PurchaseOrders.Write, PurchaseOrders.UpdateStatus);

    public static List<string> Accountant => Sorted(
        Showrooms.Read,
        Orders.Read,
        Invoices.Read, Invoices.Write, Invoices.Pay,
        Departments.Read,
        Employees.Read,
        Employees.ViewSalary,
        Showrooms.AccessAll);

    public static List<string> ShowroomUser => Sorted(Orders.Read, Invoices.Read);
    
    public static List<string> User => Sorted(
        Showrooms.Read, Products.Read,
        Categories.Read, Orders.Read,
    Showrooms.AccessAll);

    private static List<string> Sorted(params string[] codes) =>
        codes.Order(StringComparer.Ordinal).ToList();
}