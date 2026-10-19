namespace ErpDashboard.Api.Features.Inventory.Products;

public class ProductService(AppDbContext dbContext) : IProductService
{
    private static readonly SortMap<Product> Sorts = new SortMap<Product>()
        .Add("name", c => c.Name)
        .Add("sku", c => c.Sku)
        .Default("name")
        .TieBreaker(c => c.Id);

    public Task<PagedResult<ProductResponse>> ListAsync(ProductQuery query, CancellationToken ct)
    {
        var q = dbContext.Products.AsNoTracking();
        if (query.GetSearchPattern() is { } p)
            q = q.Where(product =>
                EF.Functions.ILike(product.Name, p, @"\") ||
                EF.Functions.ILike(product.Sku, p, @"\")
                );

        if (query.StockQuantity is { } stock)  q = q.Where(o => o.StockQuantity == stock);
        if (query.UnitPrice is { } price)  q = q.Where(o => o.UnitPrice == price);
        if (query.CategoryId is { } categoryId)  q = q.Where(o => o.CategoryId == categoryId);

        return Sorts.Apply(q, query.SortBy, query.SortDir)
            .Select(product => ToResponse(product))
            .ToPagedResultAsync(query, ct);
    }

    public async Task<ProductDetailsResponse> GetByIdAsync(Guid id, CancellationToken ct)
    {
        Product? product = await dbContext.Products.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (product is null)
            throw new NotFoundException("product", id);
        
        Category? category = await dbContext.Categories.FirstOrDefaultAsync(c => c.Id == product.CategoryId, ct);
        if (category is null)
            throw new NotFoundException("category", id);

        return new ProductDetailsResponse(
            product.Id,
            product.Name,
            new CategoryResponse(category.Id , category.Name),
            product.Sku,
            product.CostPrice,
            product.UnitPrice,
            product.StockQuantity,
            product.ReorderLevel);
    }

    public async Task<ProductResponse> CreateAsync(ProductRequest request, CancellationToken ct)
    {
        var existed = await dbContext.Products.AnyAsync(c => c.Sku == request.Sku, ct);

        if (existed)
            throw new BadRequestException($"Product {request.Name} already exists");


        Category? category = await dbContext.Categories.FirstOrDefaultAsync(c => c.Id == request.CategoryId, ct);
        if (category is null)
            throw new BadRequestException($"Category Id {request.CategoryId} not exists");

        Product product = new Product
        {
            Name = request.Name,
            CategoryId = request.CategoryId,
            Sku = request.Sku,
            CostPrice = request.CostPrice,
            UnitPrice = request.UnitPrice
        };
        dbContext.Products.Add(product);
        await dbContext.SaveChangesAsync(ct);
        return ToResponse(product);
    }

    public async Task<ProductResponse> UpdateAsync(Guid id, ProductRequest request, CancellationToken ct)
    {
        Product? product = await dbContext.Products.FirstOrDefaultAsync(c => c.Id == id, cancellationToken: ct);
        if (product is null)
            throw new NotFoundException("Product", id);
        
        Category? category = await dbContext.Categories.FirstOrDefaultAsync(c => c.Id == request.CategoryId, ct);
        if (category is null)
            throw new BadRequestException($"Category Id {request.CategoryId} not exists");
        
        product.Name = request.Name;
        product.CategoryId = request.CategoryId;
        product.Sku = request.Sku;
        product.CostPrice = request.CostPrice;
        product.UnitPrice = request.UnitPrice;
        await dbContext.SaveChangesAsync(ct);
        return ToResponse(product);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        Product? product = await dbContext.Products.FirstOrDefaultAsync(c => c.Id == id, cancellationToken: ct);
        if (product is null)
            throw new NotFoundException("Product", id);

        dbContext.Products.Remove(product);
        await dbContext.SaveChangesAsync(ct);
    }

    private static ProductResponse ToResponse(Product product) => new ProductResponse(
        product.Id,
        product.Name,
        product.CategoryId,
        product.Sku,
        product.CostPrice,
        product.UnitPrice,
        product.StockQuantity,
        product.ReorderLevel);
}