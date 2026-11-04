namespace ErpDashboard.Api.Features.Inventory.Categories;

public class CategoryService(AppDbContext dbContext) : ICategoryService
{
    private static readonly SortMap<Category> Sorts = new SortMap<Category>()
        .Add("name", c => c.Name)
        .Default("name")
        .TieBreaker(c => c.Id);

    public Task<PagedResult<CategoryResponse>> ListAsync(CategoryQuery query, CancellationToken ct)
    {
        var q = dbContext.Categories.AsNoTracking();
        if (query.GetSearchPattern() is { } p)
            q = q.Where(c =>
                EF.Functions.ILike(c.Name, p, @"\"));

        return Sorts.Apply(q, query.SortBy, query.SortDir)
            .Select(c => new CategoryResponse(c.Id, c.Name))
            .ToPagedResultAsync(query, ct);
    }

    public async Task<CategoryResponse> GetByIdAsync(Guid id, CancellationToken ct)
    {
        Category? category = await dbContext.Categories.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (category is null)
            throw new NotFoundException("Category", id);

        return new CategoryResponse(category.Id, category.Name);
    }

    public async Task<CategoryResponse> CreateAsync(CategoryRequest request, CancellationToken ct)
    {
        var existed = await dbContext.Categories.AnyAsync(c => c.Name == request.Name, ct);

        if (existed)
            throw new BadRequestException($"Category {request.Name} already exists");

        Category category = new Category
        {
            Name = request.Name
        };
        dbContext.Categories.Add(category);
        await dbContext.SaveChangesAsync(ct);
        return new CategoryResponse(category.Id, category.Name);
    }

    public async Task<CategoryResponse> UpdateAsync(Guid id, CategoryRequest request, CancellationToken ct)
    {
        Category? category = await dbContext.Categories.FirstOrDefaultAsync(c => c.Id == id, cancellationToken: ct);
        if (category is null)
            throw new NotFoundException("Category", id);
        category.Name = request.Name;
        await dbContext.SaveChangesAsync(ct);
        return new CategoryResponse(category.Id, category.Name);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        Category? category = await dbContext.Categories.FirstOrDefaultAsync(c => c.Id == id, cancellationToken: ct);
        if (category is null)
            throw new NotFoundException("Category", id);

        bool productsExist = await dbContext.Products.IgnoreQueryFilters().AnyAsync(p => p.CategoryId == id, ct);
        if (productsExist)
            throw new BadRequestException("Cannot delete Category contains products");

        dbContext.Categories.Remove(category);
        await dbContext.SaveChangesAsync(ct);
    }
}