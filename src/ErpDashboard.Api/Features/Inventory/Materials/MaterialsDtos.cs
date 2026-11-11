namespace ErpDashboard.Api.Features.Inventory.Materials;

public sealed record MaterialRequest([Required, MaxLength(100)] string Name);
public sealed record MaterialResponse(Guid Id, string Name);