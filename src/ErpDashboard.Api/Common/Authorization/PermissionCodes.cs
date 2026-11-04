

namespace ErpDashboard.Api.Common.Authorization;

public static class PermissionCodes
{
    public static class Users
    {
        public const string Read  = "users.read";
        public const string Write = "users.write";
    }

    public static class Roles
    {
        public const string Read  = "roles.read";
        public const string Write = "roles.write";
    }

    public static class Customers
    {
        public const string Read  = "customers.read";
        public const string Write = "customers.write";
    }

    public static class Products
    {
        public const string Read        = "products.read";
        public const string Write       = "products.write";
        public const string AdjustStock = "products.adjust-stock";
    }

    public static class Suppliers
    {
        public const string Read  = "suppliers.read";
        public const string Write = "suppliers.write";
    }

    public static class PurchaseOrders
    {
        public const string Read         = "purchase-orders.read";
        public const string Write        = "purchase-orders.write";
        public const string UpdateStatus = "purchase-orders.update-status";
    }

    public static class Orders
    {
        public const string Read         = "orders.read";
        public const string Write        = "orders.write";
        public const string UpdateStatus = "orders.update-status";
    }

    public static class Invoices
    {
        public const string Read  = "invoices.read";
        public const string Write = "invoices.write";
        public const string Pay   = "invoices.pay";
    }

    public static class Employees
    {
        public const string Read       = "employees.read";
        public const string Write      = "employees.write";
        public const string ViewSalary = "employees.view-salary";
    }
    
    public static readonly IReadOnlyList<string> All = typeof(PermissionCodes)
        .GetNestedTypes(BindingFlags.Public)
        .SelectMany(t => t.GetFields(BindingFlags.Public | BindingFlags.Static))
        .Where(f => f.IsLiteral && f.FieldType == typeof(string))
        .Select(f => (string)f.GetRawConstantValue()!)
        .Order(StringComparer.Ordinal)
        .ToList();

    private static readonly HashSet<string> AllSet = new(All, StringComparer.Ordinal);

    public static bool IsValid(string code) => AllSet.Contains(code);
}