namespace ErpDashboard.Api.Features.Inventory.Categories;

public sealed class CategoryQuery : PageQuery;


public sealed record CategoryRequest(string Name);
public sealed record CategoryResponse(Guid Id, string Name);

