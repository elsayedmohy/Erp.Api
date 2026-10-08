namespace ErpDashboard.Api.Data.Seed;

public static class RoleNames
{
    public const string Admin = "Admin";
    public const string Manager = "Manager";
    public const string InventoryManager = "InventoryManager";
    public const string Accountant = "Accountant";
    public const string User = "User";
}

public static class RoleIds
{
    public static readonly Guid Admin = Guid.Parse("00000000-0000-0000-0000-000000000001");
    public static readonly Guid Manager = Guid.Parse("00000000-0000-0000-0000-000000000002");
    public static readonly Guid InventoryManager = Guid.Parse("00000000-0000-0000-0000-000000000003");
    public static readonly Guid Accountant = Guid.Parse("00000000-0000-0000-0000-000000000004");
    public static readonly Guid User = Guid.Parse("00000000-0000-0000-0000-000000000005");
}