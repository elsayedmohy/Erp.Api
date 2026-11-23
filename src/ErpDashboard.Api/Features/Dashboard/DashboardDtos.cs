using System.ComponentModel;

namespace ErpDashboard.Api.Features.Dashboard;

[ImmutableObject(true)]
public sealed record DashboardResponse(
    DateTime GeneratedAt,
    DateOnly From,
    DateOnly To,
    IReadOnlyList<MonthlySales> MonthlySales,
    IReadOnlyList<TopProduct> TopProducts,
    IReadOnlyList<TopShowroom> TopShowrooms,
    OverdueSummary Overdue,
    BelowReorderLevel BelowReorderLevel);

public sealed record MonthlySales(int Year, int Month, decimal NetSales);

public sealed record TopProduct(Guid ProductId, string Name, string Sku, decimal NetSales);

public sealed record TopShowroom(Guid ShowroomId, string Name, decimal NetSales);

public sealed record OverdueSummary(int Count, decimal Amount);

public sealed record BelowReorderLevel(int Count, IReadOnlyList<LowStockItem> Items);