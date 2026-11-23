using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Validation;
using ErpDashboard.Api.Data;
using ErpDashboard.Api.Data.Entities;
using ErpDashboard.Api.Data.Enums;
using ErpDashboard.Api.Data.Seed;
using ErpDashboard.Api.Features.Exports;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ErpDashboard.Api.IntegrationTests;

// The export worker is off in tests (Exports:Worker:Enabled = false); tests run it with DrainAsync.
// Invoices here are dated in 2007, a year no other test or demo data uses, so the file holds only them.
[Collection(ApiCollection.Name)]
public class InvoiceExportTests(ApiFactory factory)
{
    private const int Year = 2007;
    private const string XlsxType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private readonly HttpClient _client = factory.CreateClient();

    // ---------- the happy path ----------

    [Fact]
    public async Task Start_ReturnsAtOnce_ThenTheWorkerBuildsAValidWorkbook()
    {
        var accountant = await TokenAsync(RoleIds.Accountant);
        var showroomId = await SeedShowroomAsync();
        var paidNumber = await IssuedInvoiceAsync(accountant, showroomId, unitPrice: 1000m, pay: 1140m);
        var unpaidNumber = await IssuedInvoiceAsync(accountant, showroomId, unitPrice: 500m, pay: null);
        await DraftInvoiceAsync(accountant, showroomId);   // no number: not exported

        var started = await SendAsync(HttpMethod.Post, "/api/exports/invoices", accountant, new { year = Year });
        Assert.Equal(HttpStatusCode.Accepted, started.StatusCode);
        Assert.NotNull(started.Headers.Location);
        var job = await ReadJsonAsync(started);
        Assert.Equal("Pending", job.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, job.GetProperty("downloadUrl").ValueKind);

        await DrainAsync();

        var done = await GetJsonAsync(accountant, started.Headers.Location!.ToString());
        Assert.Equal("Completed", done.GetProperty("status").GetString());
        Assert.Equal(await NumberedInvoicesInYearAsync(), done.GetProperty("rowCount").GetInt32());
        Assert.True(done.GetProperty("fileSize").GetInt64() > 0);

        var file = await SendAsync(HttpMethod.Get, done.GetProperty("downloadUrl").GetString()!, accountant);
        Assert.Equal(HttpStatusCode.OK, file.StatusCode);
        Assert.Equal(XlsxType, file.Content.Headers.ContentType?.MediaType);
        Assert.Equal($"invoices-{Year}.xlsx", file.Content.Headers.ContentDisposition?.FileNameStar
                                              ?? file.Content.Headers.ContentDisposition?.FileName?.Trim('"'));

        var rows = ReadWorkbook(await file.Content.ReadAsByteArrayAsync());
        Assert.Equal(
            ["Number", "Invoice date", "Due date", "Showroom", "Tax ID", "Status", "Payment status",
             "Total", "Paid", "Credited", "Balance"],
            rows[0]);
        Assert.Equal(done.GetProperty("rowCount").GetInt32(), rows.Count - 1);

        // 1000 + 14% VAT, paid in full.
        var paid = rows.Single(r => r[0] == paidNumber);
        Assert.Equal(("Issued", "Paid"), (paid[5], paid[6]));
        Assert.Equal((1140m, 1140m, 0m, 0m), (Money(paid[7]), Money(paid[8]), Money(paid[9]), Money(paid[10])));

        // 500 + 14% VAT, nothing paid, due in 2007.
        var unpaid = rows.Single(r => r[0] == unpaidNumber);
        Assert.Equal("Overdue", unpaid[6]);
        Assert.Equal((570m, 0m, 570m), (Money(unpaid[7]), Money(unpaid[8]), Money(unpaid[10])));

        // Dates are real Excel dates (serial numbers), not text.
        Assert.Equal(new DateOnly(Year, 3, 10), DateOnly.FromDayNumber(new DateOnly(1899, 12, 30).DayNumber + int.Parse(paid[1]!)));
    }

    private static decimal Money(string? cell) =>
        decimal.Parse(cell!, System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public async Task FileIsNotReady_UntilTheJobCompletes()
    {
        var accountant = await TokenAsync(RoleIds.Accountant);
        var job = await StartAsync(accountant);

        var early = await SendAsync(HttpMethod.Get, $"/api/exports/{job}/file", accountant);

        Assert.Equal(HttpStatusCode.Conflict, early.StatusCode);
        Assert.Equal("export_not_ready", (await ReadJsonAsync(early)).GetProperty("code").GetString());
        await DrainAsync();
    }

    [Fact]
    public async Task AskingTwice_WhileTheFirstIsWaiting_ReturnsTheSameJob()
    {
        var accountant = await TokenAsync(RoleIds.Accountant);

        var first = await StartAsync(accountant);
        var second = await StartAsync(accountant);

        Assert.Equal(first, second);
        await DrainAsync();
    }

    // ---------- surviving a restart ----------

    [Fact]
    public async Task AbandonedJob_IsPickedUpAgain_WhenItsLeaseRunsOut()
    {
        // What a crash mid-file leaves behind: Running, one attempt, a lease that has passed.
        var accountant = await TokenAsync(RoleIds.Accountant);
        var id = await StartAsync(accountant);
        await SetJobAsync(id, ExportStatus.Running, leaseUntil: DateTime.UtcNow.AddMinutes(-1), attempts: 1);

        await DrainAsync();

        var job = await JobAsync(id);
        Assert.Equal(ExportStatus.Completed, job.Status);
        Assert.Equal(2, job.Attempts);
        Assert.True(File.Exists(ExportService.FilePath(factory.ExportsDirectory, id)));
    }

    [Fact]
    public async Task RunningJob_WithALiveLease_IsLeftToTheWorkerThatHasIt()
    {
        var accountant = await TokenAsync(RoleIds.Accountant);
        var id = await StartAsync(accountant);
        await SetJobAsync(id, ExportStatus.Running, leaseUntil: DateTime.UtcNow.AddHours(1), attempts: 1);

        await DrainAsync();

        var job = await JobAsync(id);
        Assert.Equal(ExportStatus.Running, job.Status);
        Assert.Equal(1, job.Attempts);
        await SetJobAsync(id, ExportStatus.Failed, leaseUntil: null, attempts: 1);   // leave nothing running
    }

    [Fact]
    public async Task JobThatKeepsFailing_StopsAfterMaxAttempts()
    {
        var accountant = await TokenAsync(RoleIds.Accountant);
        var id = await StartAsync(accountant);
        await SetJobAsync(id, ExportStatus.Running, leaseUntil: DateTime.UtcNow.AddMinutes(-1),
            attempts: ExportProcessor.MaxAttempts);

        await DrainAsync();

        var job = await JobAsync(id);
        Assert.Equal(ExportStatus.Failed, job.Status);
        Assert.Contains($"{ExportProcessor.MaxAttempts} attempts", job.Error);
    }

    // ---------- who can see what ----------

    [Fact]
    public async Task AnotherUsersExport_IsNotFound()
    {
        var owner = await TokenAsync(RoleIds.Accountant);
        var other = await TokenAsync(RoleIds.Accountant);
        var id = await StartAsync(owner);
        await DrainAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(HttpMethod.Get, $"/api/exports/{id}", other)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(HttpMethod.Get, $"/api/exports/{id}/file", other)).StatusCode);
    }

    [Theory]
    [InlineData("Accountant", HttpStatusCode.Accepted)]
    [InlineData("Admin", HttpStatusCode.Accepted)]
    [InlineData("Sales", HttpStatusCode.Forbidden)]
    [InlineData("ShowroomUser", HttpStatusCode.Forbidden)]
    public async Task OnlyFinanceCanExport(string role, HttpStatusCode expected)
    {
        var roleId = role switch
        {
            "Accountant" => RoleIds.Accountant,
            "Admin" => RoleIds.Admin,
            "Sales" => RoleIds.Sales,
            _ => RoleIds.ShowroomUser
        };
        var token = await TokenAsync(roleId);

        var response = await SendAsync(HttpMethod.Post, "/api/exports/invoices", token, new { year = Year });

        Assert.Equal(expected, response.StatusCode);
        await DrainAsync();
    }

    [Theory]
    [InlineData(1999)]
    [InlineData(2101)]
    public async Task YearOutOfRange_IsValidationError(int year)
    {
        var accountant = await TokenAsync(RoleIds.Accountant);

        var response = await SendAsync(HttpMethod.Post, "/api/exports/invoices", accountant, new { year });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---------- reading the workbook ----------

    /// Validates the file against the Open XML schema (what Excel would refuse or "repair"), then
    /// returns every row as text: inline strings as written, numbers as stored.
    private static List<string?[]> ReadWorkbook(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var document = SpreadsheetDocument.Open(stream, false);

        var errors = new OpenXmlValidator().Validate(document).Select(e => $"{e.Path?.XPath}: {e.Description}").ToList();
        Assert.True(errors.Count == 0, "Invalid workbook:\n" + string.Join("\n", errors));

        var sheet = document.WorkbookPart!.WorksheetParts.Single().Worksheet;
        return sheet.Descendants<Row>()
            .Select(row => row.Elements<Cell>()
                .Select(c => c.InlineString?.Text?.Text ?? c.CellValue?.Text)
                .ToArray())
            .ToList();
    }

    // ---------- helpers ----------

    private async Task DrainAsync()
    {
        while (true)
        {
            using var scope = factory.Services.CreateScope();
            var processor = scope.ServiceProvider.GetRequiredService<ExportProcessor>();
            if (!await processor.ProcessNextAsync(CancellationToken.None))
                return;
        }
    }

    private async Task<Guid> StartAsync(string token)
    {
        var response = await SendAsync(HttpMethod.Post, "/api/exports/invoices", token, new { year = Year });
        Assert.True(response.StatusCode == HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync());
        return (await ReadJsonAsync(response)).GetProperty("id").GetGuid();
    }

    private async Task<string> IssuedInvoiceAsync(string token, Guid showroomId, decimal unitPrice, decimal? pay)
    {
        var id = await DraftInvoiceAsync(token, showroomId, unitPrice);

        var issued = await SendAsync(HttpMethod.Patch, $"/api/invoices/{id}/issue", token);
        Assert.True(issued.StatusCode == HttpStatusCode.OK, await issued.Content.ReadAsStringAsync());

        if (pay is { } amount)
        {
            var paid = await SendAsync(HttpMethod.Post, $"/api/invoices/{id}/payments", token,
                new { amount, method = "Cash" });
            Assert.True(paid.IsSuccessStatusCode, await paid.Content.ReadAsStringAsync());
        }

        return (await GetJsonAsync(token, $"/api/invoices/{id}")).GetProperty("number").GetString()!;
    }

    private async Task<Guid> DraftInvoiceAsync(string token, Guid showroomId, decimal unitPrice = 300m)
    {
        var response = await SendAsync(HttpMethod.Post, "/api/invoices", token, new
        {
            showroomId,
            invoiceDate = new DateOnly(Year, 3, 10),
            dueDate = new DateOnly(Year, 4, 9),
            lines = new[] { new { description = "Export check", quantity = 1, unitPrice, discount = 0m } }
        });
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await ReadJsonAsync(response)).GetProperty("id").GetGuid();
    }

    private async Task<Guid> SeedShowroomAsync()
    {
        var key = Guid.NewGuid().ToString("N")[..10];
        var showroom = new Showroom
        {
            Id = Guid.NewGuid(), Name = $"Export {key}", PhoneNumber = "01001234567",
            Address = "1 Test St", City = "Damietta", Country = "Egypt", TaxId = $"EXP-{key}"
        };
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Showrooms.Add(showroom);
        await db.SaveChangesAsync();
        return showroom.Id;
    }

    private async Task<int> NumberedInvoicesInYearAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Invoices.IgnoreQueryFilters()
            .CountAsync(i => i.Number != null && i.InvoiceDate.Year == Year);
    }

    private async Task<ExportJob> JobAsync(Guid id)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.ExportJobs.AsNoTracking().SingleAsync(j => j.Id == id);
    }

    private async Task SetJobAsync(Guid id, ExportStatus status, DateTime? leaseUntil, int attempts)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.ExportJobs.Where(j => j.Id == id).ExecuteUpdateAsync(s => s
            .SetProperty(j => j.Status, status)
            .SetProperty(j => j.LeaseUntil, leaseUntil)
            .SetProperty(j => j.Attempts, attempts));
    }

    private async Task<JsonElement> GetJsonAsync(string token, string url)
    {
        var response = await SendAsync(HttpMethod.Get, url, token);
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{url}: {await response.Content.ReadAsStringAsync()}");
        return await ReadJsonAsync(response);
    }

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body);
        return _client.SendAsync(request);
    }

    private async Task<string> TokenAsync(Guid roleId)
    {
        var user = await factory.CreateUserAsync(roleId);
        var response = await _client.PostAsJsonAsync("/api/auth/login", new { email = user.Email, password = user.Password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await ReadJsonAsync(response)).GetProperty("accessToken").GetString()!;
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.Clone();
    }
}
