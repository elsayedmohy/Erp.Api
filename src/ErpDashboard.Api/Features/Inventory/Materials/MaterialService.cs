namespace ErpDashboard.Api.Features.Inventory.Materials;

public class MaterialService(AppDbContext dbContext) : IMaterialService
{
    public async Task<IReadOnlyList<MaterialResponse>> ListAsync(CancellationToken ct) =>
        await dbContext.Materials.AsNoTracking()
            .OrderBy(m => m.Name)
            .Select(m => new MaterialResponse(m.Id, m.Name))
            .ToListAsync(ct);

    public async Task<MaterialResponse> CreateAsync(MaterialRequest request, CancellationToken ct)
    {
        var name = request.Name.Trim();
        await EnsureNameIsFreeAsync(name, excludeId: null, ct);

        var material = new Material { Id = Guid.NewGuid(), Name = name };
        dbContext.Materials.Add(material);
        await dbContext.SaveChangesAsync(ct);
        return new MaterialResponse(material.Id, material.Name);
    }

    public async Task<MaterialResponse> UpdateAsync(Guid id, MaterialRequest request, CancellationToken ct)
    {
        var material = await dbContext.Materials.FirstOrDefaultAsync(m => m.Id == id, ct)
                       ?? throw new NotFoundException("Material", id);

        var name = request.Name.Trim();
        await EnsureNameIsFreeAsync(name, excludeId: id, ct);

        material.Name = name;
        await dbContext.SaveChangesAsync(ct);
        return new MaterialResponse(material.Id, material.Name);
    }

    private async Task EnsureNameIsFreeAsync(string name, Guid? excludeId, CancellationToken ct)
    {
        var lowered = name.ToLower();
        if (await dbContext.Materials.AnyAsync(m => m.Name.ToLower() == lowered && m.Id != excludeId, ct))
            throw new ConflictException($"Material '{name}' already exists.");
    }
}