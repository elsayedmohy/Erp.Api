namespace ErpDashboard.Api.Common.Caching;

// A number per cached dataset, kept in the database. Cache keys include it, so bumping it makes every
public interface ICacheVersions
{
    Task<long> CurrentAsync(string key, CancellationToken ct);

    // Call inside the transaction that makes the change. Readers see the new version only after the
    // change is committed, so nobody can cache the old data under the new version.
    Task BumpAsync(string key, CancellationToken ct);
}

public sealed class CacheVersions(AppDbContext dbContext) : ICacheVersions
{
    public async Task<long> CurrentAsync(string key, CancellationToken ct) =>
        await dbContext.CacheVersions.AsNoTracking()
            .Where(v => v.Key == key)
            .Select(v => (long?)v.Version)
            .FirstOrDefaultAsync(ct) ?? 0;

    public Task BumpAsync(string key, CancellationToken ct) =>
        dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "CacheVersions" ("Key", "Version") VALUES ({key}, 1)
            ON CONFLICT ("Key") DO UPDATE SET "Version" = "CacheVersions"."Version" + 1
            """, ct);
}
