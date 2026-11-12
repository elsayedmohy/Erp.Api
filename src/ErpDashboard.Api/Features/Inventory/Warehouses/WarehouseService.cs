
namespace ErpDashboard.Api.Features.Inventory.Warehouses;

public sealed class WarehouseService(AppDbContext dbContext, IStockService stock,IWarehouseScope scope , ICurrentUserPermissions permissions) : IWarehouseService
{
    private static readonly SortMap<Warehouse> Sorts = new SortMap<Warehouse>()
        .Add("code", w => w.Code)
        .Add("name", w => w.Name)
        .Default("code")
        .TieBreaker(w => w.Id);

    private static readonly SortMap<WarehouseStock> StockSorts = new SortMap<WarehouseStock>()
        .Add("name", s => s.Product.Name)
        .Add("sku", s => s.Product.Sku)
        .Add("quantity", s => s.Quantity)
        .Default("name")
        .TieBreaker(s => s.ProductId);

    public Task<PagedResult<WarehouseResponse>> ListAsync(WarehouseQuery query, CancellationToken ct)
    {
        var q = dbContext.Warehouses.AsNoTracking();
        var accessible = scope.AccessibleWarehouseIds();
        q=  q.Where(w => accessible.Contains(w.Id));
        if (query.GetSearchPattern() is { } p)
            q = q.Where(w => EF.Functions.ILike(w.Code, p, @"\") || EF.Functions.ILike(w.Name, p, @"\"));

        return Sorts.Apply(q, query.SortBy, query.SortDir)
            .Select(w => new WarehouseResponse(w.Id, w.Code, w.Name))
            .ToPagedResultAsync(query, ct);
    }

    public async Task<WarehouseResponse> GetByIdAsync(Guid id, CancellationToken ct)
    {
        await EnsureAccessibleAsync(id, ct);
       return await dbContext.Warehouses.AsNoTracking()
                .Where(w => w.Id == id)
                .Select(w => new WarehouseResponse(w.Id, w.Code, w.Name))
                .SingleOrDefaultAsync(ct)
            ?? throw new NotFoundException("Warehouse", id);
    }


    public async Task<WarehouseResponse> CreateAsync(WarehouseRequest request, Guid createdById,CancellationToken ct)
    {
        var code = NormalizeCode(request.Code);
        await EnsureCodeIsFreeAsync(code, excludeId: null, ct);

        var warehouse = new Warehouse { Code = code, Name = request.Name.Trim() };
        dbContext.Warehouses.Add(warehouse);
        if (!scope.HasAccessAll)
            dbContext.UserWarehouses.Add(new UserWarehouse { UserId = createdById, WarehouseId = warehouse.Id });
        await dbContext.SaveChangesAsync(ct);

        return new WarehouseResponse(warehouse.Id, warehouse.Code, warehouse.Name);
    }

    public async Task<WarehouseResponse> UpdateAsync(Guid id, WarehouseRequest request, CancellationToken ct)
    {
        await EnsureAccessibleAsync(id, ct);
        var warehouse = await dbContext.Warehouses.FirstOrDefaultAsync(w => w.Id == id, ct)
                        ?? throw new NotFoundException("Warehouse", id);

        var code = NormalizeCode(request.Code);
        await EnsureCodeIsFreeAsync(code, excludeId: id, ct);

        warehouse.Code = code;
        warehouse.Name = request.Name.Trim();
        await dbContext.SaveChangesAsync(ct);

        return new WarehouseResponse(warehouse.Id, warehouse.Code, warehouse.Name);
    }

    public async Task<PagedResult<WarehouseStockResponse>> ListStockAsync(
        Guid id, WarehouseStockQuery query, CancellationToken ct)
    {
        await EnsureAccessibleAsync(id, ct);

        var q = dbContext.WarehouseStocks.AsNoTracking().Where(s => s.WarehouseId == id);

        if (query.GetSearchPattern() is { } p)
            q = q.Where(s => EF.Functions.ILike(s.Product.Name, p, @"\") ||
                             EF.Functions.ILike(s.Product.Sku, p, @"\"));

        if (query.LowStock == true)
            q = q.Where(s => s.Product.ReorderLevel > 0 && s.Quantity < s.Product.ReorderLevel);

        return await StockSorts.Apply(q, query.SortBy, query.SortDir)
            .Select(s => new WarehouseStockResponse(
                s.ProductId,
                s.Product.Name,
                s.Product.Sku,
                s.Quantity,
                s.Product.ReorderLevel))
            .ToPagedResultAsync(query, ct);
    }

    public async Task<AdjustStockResponse> AdjustStockAsync(
        Guid id, AdjustStockRequest request, Guid adjustedById, CancellationToken ct)
    {
        await EnsureAccessibleAsync(id, ct);

        if (request.Delta == 0)
            throw new RequestValidationException("delta", "Delta must not be zero.");

        var productId = request.ProductId!.Value;
        if (!await dbContext.Products.AnyAsync(p => p.Id == productId, ct))
            throw new RequestValidationException("productId", $"Product '{productId}' does not exist.");

        await using var transaction = await dbContext.Database.BeginTransactionAsync(ct);

        var applied = await stock.TryApplyAsync(new StockChange(
            id, productId, request.Delta, StockMovementType.Adjustment, adjustedById,
            Reason: request.Reason.Trim()), ct);

        if (!applied)
            throw new ConflictException("The adjustment would make stock negative.");

        var quantity = await dbContext.WarehouseStocks
            .Where(s => s.WarehouseId == id && s.ProductId == productId)
            .Select(s => s.Quantity)
            .SingleAsync(ct);

        await transaction.CommitAsync(ct);
        return new AdjustStockResponse(id, productId, quantity);
    }

    private async Task EnsureAccessibleAsync(Guid id, CancellationToken ct)
    {
        if (!await scope.CanAccessAsync(id, ct))
            throw new NotFoundException("Warehouse", id);
    }

    private async Task EnsureCodeIsFreeAsync(string code, Guid? excludeId, CancellationToken ct)
    {
        if (await dbContext.Warehouses.AnyAsync(w => w.Code == code && w.Id != excludeId, ct))
            throw new ConflictException($"Warehouse code '{code}' already exists.");
    }

    private static string NormalizeCode(string code) => code.Trim().ToUpperInvariant();
}