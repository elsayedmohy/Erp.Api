namespace ErpDashboard.Api.Features.Inventory.Products;

public class ProductService(AppDbContext dbContext , IWarehouseScope scope) : IProductService
{
    private static readonly SortMap<Product> Sorts = new SortMap<Product>()
        .Add("name", c => c.Name)
        .Add("sku", c => c.Sku)
        .Add("unitPrice", c => c.UnitPrice)
        .Default("name")
        .TieBreaker(c => c.Id);

    public Task<PagedResult<ProductResponse>> ListAsync(ProductQuery query, CancellationToken ct)
    {
        var q = dbContext.Products.AsNoTracking();
        var accessible = scope.AccessibleWarehouseIds();
        if (query.GetSearchPattern() is { } p)
            q = q.Where(product =>
                EF.Functions.ILike(product.Name, p, @"\") ||
                EF.Functions.ILike(product.Sku, p, @"\")
                );

        if (query.MinPrice is { } min) q = q.Where(p => p.UnitPrice >= min);
        if (query.MaxPrice is { } max) q = q.Where(p => p.UnitPrice <= max);
        if (query.CategoryId is { } categoryId)  q = q.Where(o => o.CategoryId == categoryId);

        return Sorts.Apply(q, query.SortBy, query.SortDir)
            .Select(product => new ProductResponse(
                product.Id,
                product.Name,
                product.CategoryId,
                product.Sku,
                product.CostPrice,
                product.UnitPrice,
                dbContext.WarehouseStocks
                    .Where(s => s.ProductId == product.Id && accessible.Contains(s.WarehouseId))
                    .Sum(s => (int?)s.Quantity) ?? 0,
                product.ReorderLevel))
            .ToPagedResultAsync(query, ct);
    }

    public async Task<ProductDetailsResponse> GetByIdAsync(Guid id, CancellationToken ct)
    {
        Product? product = await dbContext.Products.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (product is null)
            throw new NotFoundException("product", id);
        
        Category? category = await dbContext.Categories.FirstOrDefaultAsync(c => c.Id == product.CategoryId, ct);
        if (category is null)
            throw new NotFoundException("category", product.CategoryId);

        return new ProductDetailsResponse(
            product.Id,
            product.Name,
            new CategoryResponse(category.Id, category.Name),
            product.Sku,
            product.CostPrice,
            product.UnitPrice,
            await GetStockQuantityAsync(product.Id, ct),
            product.ReorderLevel);
    }

    public async Task<ProductResponse> CreateAsync(ProductRequest request, CancellationToken ct)
    {
        var sku = NormalizeSku(request.Sku);
        await EnsureSkuIsFreeAsync(sku, excludeId: null, ct);


        if (!await dbContext.Categories.AnyAsync(c => c.Id == request.CategoryId, ct))
            throw new RequestValidationException("categoryId", $"Category '{request.CategoryId}' does not exist.");

        Product product = new Product
        {
            Name = request.Name.Trim(),
            CategoryId = request.CategoryId,
            Sku = sku,
            CostPrice = request.CostPrice,
            UnitPrice = request.UnitPrice,
            ReorderLevel = request.ReorderLevel
        };
        dbContext.Products.Add(product);
        await dbContext.SaveChangesAsync(ct);
        return ToResponse(product , 0);
    }

    public async Task<ProductResponse> UpdateAsync(Guid id, ProductRequest request, CancellationToken ct)
    {
        var product = await dbContext.Products.FirstOrDefaultAsync(c => c.Id == id, ct)
                      ?? throw new NotFoundException("Product", id);

        var sku = NormalizeSku(request.Sku);
        await EnsureSkuIsFreeAsync(sku, excludeId: id, ct);

        if (!await dbContext.Categories.AnyAsync(c => c.Id == request.CategoryId, ct))
            throw new RequestValidationException("categoryId", $"Category '{request.CategoryId}' does not exist.");
        
        product.Name = request.Name.Trim();
        product.CategoryId = request.CategoryId;
        product.Sku = sku;
        product.CostPrice = request.CostPrice;
        product.UnitPrice = request.UnitPrice;
        product.ReorderLevel = request.ReorderLevel;
        
        await dbContext.SaveChangesAsync(ct);
        return ToResponse(product , await GetStockQuantityAsync(product.Id, ct));
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

        if (await dbContext.Orders
                .IgnoreQueryFilters([QueryFilters.Showroom])
                .AnyAsync(o =>
                    (o.Status == OrderStatus.Pending || o.Status == OrderStatus.PendingApproval) &&
                    o.OrderLines.Any(l => l.ProductId == id), ct))
            throw new ConflictException("Cannot delete a product on an open order.");

        dbContext.Products.Remove(product);
        await dbContext.SaveChangesAsync(ct);
    }

    private Task<int> GetStockQuantityAsync(Guid productId, CancellationToken ct)
    {
        var accessible = scope.AccessibleWarehouseIds();
        return dbContext.WarehouseStocks
            .Where(s => s.ProductId == productId && accessible.Contains(s.WarehouseId))
            .SumAsync(s => s.Quantity, ct);
    }
    
    private static string NormalizeSku(string sku) => sku.Trim().ToUpperInvariant();

    private async Task EnsureSkuIsFreeAsync(string sku, Guid? excludeId, CancellationToken ct)
    {
        if (await dbContext.Products.AnyAsync(p => p.Sku == sku && p.Id != excludeId, ct))
            throw new ConflictException($"SKU '{sku}' is already used by another product.");
    }
    private static ProductResponse ToResponse(Product product, int stockQuantity) => new(
        product.Id,
        product.Name,
        product.CategoryId,
        product.Sku,
        product.CostPrice,
        product.UnitPrice,
        stockQuantity,
        product.ReorderLevel);
}