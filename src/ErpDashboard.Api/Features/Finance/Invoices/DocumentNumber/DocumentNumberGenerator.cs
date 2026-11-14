namespace ErpDashboard.Api.Features.Finance.Invoices.DocumentNumber;


public interface IDocumentNumberGenerator
{
    Task<string> NextAsync(string series, int year, CancellationToken ct);
}

public sealed class DocumentNumberGenerator(AppDbContext dbContext) : IDocumentNumberGenerator
{
    public async Task<string> NextAsync(string series, int year, CancellationToken ct)
    {
        if (dbContext.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Document numbers must be taken inside a transaction.");

        var next = await dbContext.Database.SqlQuery<int>($"""
                                                           INSERT INTO "DocumentNumberCounters" ("Series", "Year", "LastNumber")
                                                           VALUES ({series}, {year}, 1)
                                                           ON CONFLICT ("Series", "Year")
                                                           DO UPDATE SET "LastNumber" = "DocumentNumberCounters"."LastNumber" + 1
                                                           RETURNING "LastNumber" AS "Value"
                                                           """).ToListAsync(ct);

        return $"{series}-{year}-{next.Single():D6}";
    }
}