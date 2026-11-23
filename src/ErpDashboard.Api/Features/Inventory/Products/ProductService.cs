namespace ErpDashboard.Api.Features.Inventory.Products;

public class ProductService(
    AppDbContext dbContext,
    IWarehouseScope scope,
    ICurrentUserPermissions permissions,
    IProductCatalog catalog,
    ICacheVersions versions) : IProductService
{
    private bool CanViewCost => permissions.Has(PermissionCodes.Products.ViewCost);

    private void EnsureCanWriteCost(decimal? cost)
    {
        if (cost is not null && !CanViewCost)
            throw new ForbiddenException("You do not have permission to set product cost.");
    }

    private static readonly SortMap<CatalogItem> Sorts = new SortMap<CatalogItem>()
        .Add("name", c => c.Name)
        .Add("sku", c => c.Sku)
        .Add("unitPrice", c => c.UnitPrice)
        .Default("name")
        .TieBreaker(c => c.Id);

    public async Task<PagedResult<ProductResponse>> ListAsync(ProductQuery query, CancellationToken ct)
    {
        IEnumerable<CatalogItem> items = (await catalog.GetAsync(ct)).Items;

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            items = items.Where(p => p.Name.Contains(term, StringComparison.OrdinalIgnoreCase)
                                     || p.Sku.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        if (query.MinPrice is { } min) items = items.Where(p => p.UnitPrice >= min);
        if (query.MaxPrice is { } max) items = items.Where(p => p.UnitPrice <= max);
        if (query.CategoryId is { } categoryId) items = items.Where(p => p.CategoryId == categoryId);
        if (query.MaterialId is { } materialId) items = items.Where(p => p.MaterialId == materialId);

        var sorted = Sorts.Apply(items.AsQueryable(), query.SortBy, query.SortDir);
        var total = sorted.Count();
        var page = sorted.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToList();

        // Stock changes with every order: read live, and only for the products on this page.
        var ids = page.Select(p => p.Id).ToList();
        var accessible = scope.AccessibleWarehouseIds();
        var stock = await dbContext.WarehouseStocks.AsNoTracking()
            .Where(s => ids.Contains(s.ProductId) && accessible.Contains(s.WarehouseId))
            .GroupBy(s => s.ProductId)
            .Select(g => new { ProductId = g.Key, Available = g.Sum(s => s.Quantity - s.Reserved) })
            .ToDictionaryAsync(x => x.ProductId, x => x.Available, ct);

        // One cached copy for everyone: cost is removed here, per request, not per cache entry.
        var canViewCost = CanViewCost;
        return new PagedResult<ProductResponse>(
            page.Select(p => new ProductResponse(
                p.Id, p.Name, p.CategoryId, p.Sku,
                canViewCost ? p.CostPrice : null,
                p.UnitPrice,
                stock.GetValueOrDefault(p.Id),
                p.ReorderLevel, p.LengthCm, p.WidthCm, p.HeightCm, p.WeightKg, p.MaterialId)).ToList(),
            query.Page, query.PageSize, total);
    }

    public async Task<ProductDetailsResponse> GetByIdAsync(Guid id, CancellationToken ct)
    {
        Product? product = await dbContext.Products.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (product is null)
            throw new NotFoundException("product", id);

        Category? category = await dbContext.Categories.FirstOrDefaultAsync(c => c.Id == product.CategoryId, ct);
        if (category is null)
            throw new NotFoundException("category", product.CategoryId);

        var material = product.MaterialId is { } mId
            ? await dbContext.Materials.Where(m => m.Id == mId)
                .Select(m => new MaterialResponse(m.Id, m.Name)).FirstOrDefaultAsync(ct)
            : null;

        return new ProductDetailsResponse(
            product.Id,
            product.Name,
            new CategoryResponse(category.Id, category.Name),
            product.Sku,
            CanViewCost ? product.CostPrice : null,
            product.UnitPrice,
            await GetStockQuantityAsync(product.Id, ct),
            product.ReorderLevel,
            product.LengthCm,
            product.WidthCm,
            product.HeightCm,
            product.WeightKg,
            material
        );
    }

    public async Task<ProductResponse> CreateAsync(ProductRequest request, CancellationToken ct)
    {
        var sku = NormalizeSku(request.Sku);
        await EnsureSkuIsFreeAsync(sku, excludeId: null, ct);
        EnsureCanWriteCost(request.CostPrice);

        if (!await dbContext.Categories.AnyAsync(c => c.Id == request.CategoryId, ct))
            throw new RequestValidationException("categoryId", $"Category '{request.CategoryId}' does not exist.");

        if (request.MaterialId is { } materialId &&
            !await dbContext.Materials.AnyAsync(m => m.Id == materialId, ct))
            throw new RequestValidationException("materialId", $"Material '{materialId}' does not exist.");

        Product product = new Product
        {
            Name = request.Name.Trim(),
            CategoryId = request.CategoryId,
            Sku = sku,
            CostPrice = request.CostPrice ?? 0m,
            UnitPrice = request.UnitPrice,
            ReorderLevel = request.ReorderLevel,
            LengthCm = request.LengthCm,
            WidthCm = request.WidthCm,
            HeightCm = request.HeightCm,
            WeightKg = request.WeightKg,
            MaterialId = request.MaterialId
        };
        dbContext.Products.Add(product);
        await SaveAndBumpCatalogAsync(ct);
        return ToResponse(product, 0, CanViewCost);
    }

    public async Task<ProductResponse> UpdateAsync(Guid id, ProductRequest request, Guid userId, CancellationToken ct)
    {
        var product = await dbContext.Products.FirstOrDefaultAsync(c => c.Id == id, ct)
                      ?? throw new NotFoundException("Product", id);

        var sku = NormalizeSku(request.Sku);
        await EnsureSkuIsFreeAsync(sku, excludeId: id, ct);
        EnsureCanWriteCost(request.CostPrice);

        if (!await dbContext.Categories.AnyAsync(c => c.Id == request.CategoryId, ct))
            throw new RequestValidationException("categoryId", $"Category '{request.CategoryId}' does not exist.");

        if (request.MaterialId is { } materialId &&
            !await dbContext.Materials.AnyAsync(m => m.Id == materialId, ct))
            throw new RequestValidationException("materialId", $"Material '{materialId}' does not exist.");

        var oldUnitPrice = product.UnitPrice;
        var oldCostPrice = product.CostPrice;
        product.Name = request.Name.Trim();
        product.CategoryId = request.CategoryId;
        product.Sku = sku;
        if (request.CostPrice is { } cost)
            product.CostPrice = cost;
        product.UnitPrice = request.UnitPrice;
        product.ReorderLevel = request.ReorderLevel;
        product.LengthCm = request.LengthCm;
        product.WidthCm = request.WidthCm;
        product.HeightCm = request.HeightCm;
        product.WeightKg = request.WeightKg;
        product.MaterialId = request.MaterialId;

        if (product.UnitPrice != oldUnitPrice || product.CostPrice != oldCostPrice)
        {
            dbContext.ProductPriceHistory.Add(new ProductPriceHistory
            {
                Id = Guid.NewGuid(),
                ProductId = product.Id,
                OldUnitPrice = oldUnitPrice,
                NewUnitPrice = product.UnitPrice,
                OldCostPrice = oldCostPrice,
                NewCostPrice = product.CostPrice,
                ChangedById = userId,
                ChangedAt = DateTime.UtcNow
            });
        }

        await SaveAndBumpCatalogAsync(ct);
        return ToResponse(product, await GetStockQuantityAsync(product.Id, ct), CanViewCost);
    }

    public async Task<IReadOnlyList<ProductPriceChangeResponse>> GetPriceHistoryAsync(Guid id, CancellationToken ct)
    {
        if (!await dbContext.Products.AnyAsync(p => p.Id == id, ct))
            throw new NotFoundException("Product", id);

        var canViewCost = CanViewCost;
        var q = dbContext.ProductPriceHistory.AsNoTracking().Where(h => h.ProductId == id);

        if (!canViewCost)
            q = q.Where(h => h.OldUnitPrice != h.NewUnitPrice);

        return await q
            .OrderByDescending(h => h.ChangedAt)
            .ThenByDescending(h => h.Id)
            .Select(h => new ProductPriceChangeResponse(
                h.OldUnitPrice,
                h.NewUnitPrice,
                canViewCost ? h.OldCostPrice : null,
                canViewCost ? h.NewCostPrice : null,
                h.ChangedById,
                h.ChangedAt))
            .ToListAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var product = await dbContext.Products.FirstOrDefaultAsync(c => c.Id == id, ct)
                      ?? throw new NotFoundException("Product", id);

        if (await dbContext.WarehouseStocks.AnyAsync(s => s.ProductId == id && s.Quantity != 0, ct))
            throw new ConflictException("Cannot delete a product that still has stock in any warehouse.");

        if (await dbContext.PurchaseOrders.AnyAsync(po =>
                po.Status == PurchaseOrderStatus.Pending &&
                po.PurchaseLines.Any(l => l.ProductId == id), ct))
            throw new ConflictException("Cannot delete a product on a pending purchase order.");

        if (await dbContext.StockTransfers.AnyAsync(t =>
                t.Status == StockTransferStatus.InTransit &&
                t.Lines.Any(l => l.ProductId == id), ct))
            throw new ConflictException("Cannot delete a product that is in transit between warehouses.");

        if (await dbContext.Orders
                .IgnoreQueryFilters([QueryFilters.Showroom])
                .AnyAsync(o =>
                    (o.Status == OrderStatus.Draft ||
                     o.Status == OrderStatus.PendingApproval ||
                     o.Status == OrderStatus.Confirmed ||
                     o.Status == OrderStatus.Preparing) &&
                    o.OrderLines.Any(l => l.ProductId == id), ct))
            throw new ConflictException("Cannot delete a product on an open order.");

        dbContext.Products.Remove(product);
        await SaveAndBumpCatalogAsync(ct);
    }

    private async Task SaveAndBumpCatalogAsync(CancellationToken ct)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(ct);
        await dbContext.SaveChangesAsync(ct);
        await versions.BumpAsync(ProductCatalog.VersionKey, ct);
        await transaction.CommitAsync(ct);
    }
    
    private Task<int> GetStockQuantityAsync(Guid productId, CancellationToken ct)
    {
        var accessible = scope.AccessibleWarehouseIds();
        return dbContext.WarehouseStocks
            .Where(s => s.ProductId == productId && accessible.Contains(s.WarehouseId))
            .SumAsync(s => s.Quantity - s.Reserved, ct);
    }

    private static string NormalizeSku(string sku) => sku.Trim().ToUpperInvariant();

    private async Task EnsureSkuIsFreeAsync(string sku, Guid? excludeId, CancellationToken ct)
    {
        if (await dbContext.Products.AnyAsync(p => p.Sku == sku && p.Id != excludeId, ct))
            throw new ConflictException($"SKU '{sku}' is already used by another product.");
    }

    private static ProductResponse ToResponse(Product product, int stockQuantity, bool canViewCost) => new(
        product.Id,
        product.Name,
        product.CategoryId,
        product.Sku,
        canViewCost ? product.CostPrice : null,
        product.UnitPrice,
        stockQuantity,
        product.ReorderLevel,
        product.LengthCm,
        product.WidthCm,
        product.HeightCm,
        product.WeightKg,
        product.MaterialId);
}