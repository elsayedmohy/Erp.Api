using ErpDashboard.Api.Common;
using ErpDashboard.Api.Data;
using ErpDashboard.Api.Data.Entities;
using Microsoft.Extensions.DependencyInjection;

namespace ErpDashboard.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class ShowroomFilterCoverageTests(ApiFactory factory)
{
    // Showroom-owned by shape but protected another way. Adding a type here is a security decision: say why.
    private static readonly Dictionary<Type, string> Exempt = new()
    {
        [typeof(User)] = "Users are guarded by users.read, which showroom users never have.",
    };

    [Fact]
    public void Every_showroom_owned_entity_has_the_showroom_filter()
    {
        using var scope = factory.Services.CreateScope();
        var model = scope.ServiceProvider.GetRequiredService<AppDbContext>().Model;

        var missing = model.GetEntityTypes()
            .Where(t => t.ClrType == typeof(Showroom) || t.FindProperty("ShowroomId") is not null)
            .Where(t => !Exempt.ContainsKey(t.ClrType))
            .Where(t => !t.GetDeclaredQueryFilters().Any(f => f.Key == QueryFilters.Showroom))
            .Select(t => t.ClrType.Name)
            .ToList();

        Assert.True(missing.Count == 0,
            $"Missing the '{QueryFilters.Showroom}' query filter: {string.Join(", ", missing)}");
    }

    [Fact]
    public void Exemptions_still_exist_in_the_model()
    {
        // A stale exemption hides nothing today but would silently exempt a future type with that name.
        using var scope = factory.Services.CreateScope();
        var model = scope.ServiceProvider.GetRequiredService<AppDbContext>().Model;

        foreach (var type in Exempt.Keys)
            Assert.NotNull(model.FindEntityType(type));
    }
}