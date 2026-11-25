using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ErpDashboard.Api.Common;
using ErpDashboard.Api.Common.Workers;
using ErpDashboard.Api.Data;
using ErpDashboard.Api.Data.Entities;
using ErpDashboard.Api.Data.Enums;
using ErpDashboard.Api.Data.Seed;
using ErpDashboard.Api.Features.Notifications;
using ErpDashboard.Api.Features.Notifications.Reminders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ErpDashboard.Api.IntegrationTests;

// The database is shared with every other test, so other showrooms may be overdue too: each test makes its
// own showroom and looks only at that showroom's reminders. Each run uses its own slot date, except the
// processor test, which uses today's.
[Collection(ApiCollection.Name)]
public class OverdueReminderTests : IDisposable
{
    // Total of an invoice with one line of 1000 and 14% tax.
    private const decimal InvoiceTotal = 1140m;
    private const string Mobile = "01001234567";
    private const string MobileOnWhatsApp = "201001234567";

    private static int _slots;

    private readonly ApiFactory _factory;
    private readonly HttpClient _client;

    public OverdueReminderTests(ApiFactory factory)
    {
        _factory = factory;
        factory.WhatsApp.Reset();
        _client = factory.CreateClient();
    }

    public void Dispose() => _factory.WhatsApp.Reset();

    [Fact]
    public async Task OverdueShowroom_GetsOneMessage_WithCountAmountAndOldestDueDate()
    {
        var token = await TokenAsync();
        var today = Today();
        var showroom = await SeedShowroomAsync(Mobile);
        await IssuedInvoiceAsync(token, showroom.Id, dueDate: today.AddDays(-30));
        var second = await IssuedInvoiceAsync(token, showroom.Id, dueDate: today.AddDays(-5));
        await PayAsync(token, second, 300m);

        var result = await SendAsync(NextSlot());

        Assert.NotNull(result);
        var reminder = Assert.Single(await RemindersAsync(showroom.Id));
        Assert.Equal(2, reminder.InvoiceCount);
        Assert.Equal(InvoiceTotal + (InvoiceTotal - 300m), reminder.Amount);
        Assert.Equal(today.AddDays(-30), reminder.OldestDueDate);

        var notification = await NotificationAsync(reminder.NotificationId);
        Assert.Equal(NotificationStatus.Pending, notification.Status);
        Assert.Equal(MobileOnWhatsApp, notification.Recipient);
        Assert.Equal(
            OverdueReminders.Message(showroom.Name, 2, 1980m, today.AddDays(-30), CompanyName()),
            notification.Body);
        Assert.Contains("1,980.00", notification.Body);

        // The outbox sends it like any other WhatsApp message.
        await DrainAsync<NotificationProcessor>();
        Assert.Contains((MobileOnWhatsApp, notification.Body), _factory.WhatsApp.Sent);
        Assert.Equal(NotificationStatus.Sent, (await NotificationAsync(reminder.NotificationId)).Status);
    }

    [Fact]
    public async Task SameSlotTwice_SendsOnce()
    {
        var token = await TokenAsync();
        var showroom = await SeedShowroomAsync(Mobile);
        await IssuedInvoiceAsync(token, showroom.Id, dueDate: Today().AddDays(-10));
        var slot = NextSlot();

        Assert.NotNull(await SendAsync(slot));
        Assert.Null(await SendAsync(slot));

        Assert.Single(await RemindersAsync(showroom.Id));
    }

    [Fact]
    public async Task TwoInstancesAtOnce_SendOnce()
    {
        var token = await TokenAsync();
        var showroom = await SeedShowroomAsync(Mobile);
        await IssuedInvoiceAsync(token, showroom.Id, dueDate: Today().AddDays(-10));
        var slot = NextSlot();

        // Separate scopes, so separate DbContexts and connections, like two app instances.
        var results = await Task.WhenAll(SendAsync(slot), SendAsync(slot));

        Assert.Single(results, r => r is not null);
        Assert.Single(await RemindersAsync(showroom.Id));
        Assert.Single(await Db(db => db.ReminderRuns.AsNoTracking().Where(r => r.Date == slot).ToListAsync()));
    }

    [Fact]
    public async Task NextWeek_StillOverdue_RemindsAgain()
    {
        var token = await TokenAsync();
        var showroom = await SeedShowroomAsync(Mobile);
        await IssuedInvoiceAsync(token, showroom.Id, dueDate: Today().AddDays(-10));

        await SendAsync(NextSlot());
        await SendAsync(NextSlot());

        Assert.Equal(2, (await RemindersAsync(showroom.Id)).Count);
    }

    [Fact]
    public async Task PaidAfterTheReminder_IsNotRemindedNextWeek()
    {
        var token = await TokenAsync();
        var showroom = await SeedShowroomAsync(Mobile);
        var invoice = await IssuedInvoiceAsync(token, showroom.Id, dueDate: Today().AddDays(-10));

        await SendAsync(NextSlot());
        await PayAsync(token, invoice, InvoiceTotal);
        await SendAsync(NextSlot());

        Assert.Single(await RemindersAsync(showroom.Id));
    }

    [Fact]
    public async Task NotDueYet_FullyPaid_FullyCredited_OrDraft_AreNotReminded()
    {
        var token = await TokenAsync();
        var today = Today();

        var notDue = await SeedShowroomAsync(Mobile);
        await IssuedInvoiceAsync(token, notDue.Id, dueDate: today.AddDays(5));

        var dueToday = await SeedShowroomAsync(Mobile);
        await IssuedInvoiceAsync(token, dueToday.Id, dueDate: today);   // overdue from tomorrow

        var paid = await SeedShowroomAsync(Mobile);
        var paidInvoice = await IssuedInvoiceAsync(token, paid.Id, dueDate: today.AddDays(-10));
        await PayAsync(token, paidInvoice, InvoiceTotal);

        var credited = await SeedShowroomAsync(Mobile);
        var creditedInvoice = await IssuedInvoiceAsync(token, credited.Id, dueDate: today.AddDays(-10));
        await CreditAllAsync(token, creditedInvoice);

        var draft = await SeedShowroomAsync(Mobile);
        await DraftInvoiceAsync(token, draft.Id, dueDate: today.AddDays(-10));

        await SendAsync(NextSlot());

        foreach (var showroom in new[] { notDue, dueToday, paid, credited, draft })
            Assert.Empty(await RemindersAsync(showroom.Id));
    }

    [Fact]
    public async Task LandlineShowroom_IsRecorded_ButSkipped()
    {
        var token = await TokenAsync();
        var showroom = await SeedShowroomAsync("0572345678");
        await IssuedInvoiceAsync(token, showroom.Id, dueDate: Today().AddDays(-10));
        var slot = NextSlot();

        var result = await SendAsync(slot);

        Assert.True(result!.Skipped >= 1);
        var reminder = Assert.Single(await RemindersAsync(showroom.Id));
        var notification = await NotificationAsync(reminder.NotificationId);
        Assert.Equal(NotificationStatus.Skipped, notification.Status);
        Assert.Null(notification.Recipient);

        var run = await Db(db => db.ReminderRuns.AsNoTracking().SingleAsync(r => r.Date == slot));
        Assert.Equal(result.Showrooms, run.Showrooms);
        Assert.Equal(result.Skipped, run.Skipped);
    }

    [Fact]
    public async Task DeletedShowroom_IsNotMessaged()
    {
        var token = await TokenAsync();
        var showroom = await SeedShowroomAsync(Mobile);
        await IssuedInvoiceAsync(token, showroom.Id, dueDate: Today().AddDays(-10));
        await Db(db => db.Showrooms.IgnoreQueryFilters([QueryFilters.Showroom])
            .Where(s => s.Id == showroom.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsDeleted, true).SetProperty(x => x.DeletedAt, DateTime.UtcNow)));

        await SendAsync(NextSlot());

        Assert.Empty(await RemindersAsync(showroom.Id));
    }

    [Fact]
    public async Task Logs_HaveCounts_NotNamesOrPhones()
    {
        var token = await TokenAsync();
        var phone = $"010{Random.Shared.Next(10_000_000, 99_999_999)}";
        var showroom = await SeedShowroomAsync(phone);
        await IssuedInvoiceAsync(token, showroom.Id, dueDate: Today().AddDays(-10));

        await SendAsync(NextSlot());

        var logs = string.Join('\n', _factory.Logs.Events.Select(e => e.RenderMessage()));
        Assert.Contains("Overdue reminders for", logs);
        Assert.DoesNotContain(showroom.Name, logs);
        Assert.DoesNotContain("1009876543", logs);
    }

    [Fact]
    public async Task Processor_SendsTheDueSlot_OnlyOnce()
    {
        var token = await TokenAsync();
        var showroom = await SeedShowroomAsync(Mobile);
        await IssuedInvoiceAsync(token, showroom.Id, dueDate: Today().AddDays(-10));

        // Today is the reminder day and its time has passed, so today's slot is due.
        var clock = _factory.Services.GetRequiredService<IBusinessClock>();
        var options = new ReminderOptions { Day = clock.Today.DayOfWeek, SendAt = TimeOnly.MinValue };

        Assert.False(await ProcessAsync(options));   // false: nothing more to do straight away
        Assert.False(await ProcessAsync(options));

        var reminder = Assert.Single(await RemindersAsync(showroom.Id));
        Assert.Equal(clock.Today, reminder.Date);
    }

    [Fact]
    public async Task Processor_BeforeTheTime_SendsNothing()
    {
        var token = await TokenAsync();
        var showroom = await SeedShowroomAsync(Mobile);
        await IssuedInvoiceAsync(token, showroom.Id, dueDate: Today().AddDays(-10));

        // The reminder day is tomorrow and no catch-up is allowed: last week's slot is too old.
        var clock = _factory.Services.GetRequiredService<IBusinessClock>();
        var options = new ReminderOptions { Day = clock.Today.AddDays(1).DayOfWeek, CatchUpDays = 0 };

        await ProcessAsync(options);

        Assert.Empty(await RemindersAsync(showroom.Id));
    }

    [Fact]
    public void Message_HasNoHiddenDirectionCharacters()
    {
        // Invisible bidi marks (U+200E/F, U+202A–E, U+2066–9) slip in when Arabic is copied between editors.
        var text = OverdueReminders.Message("Nile Furniture", 2, 100m, new DateOnly(2026, 10, 1), "Damietta Woodworks");

        Assert.DoesNotMatch("[\u200E\u200F\u202A-\u202E\u2066-\u2069]", text);
    }
    // ---------- helpers ----------

    /// A date no other run in this test run uses (ReminderRuns is keyed by date).
    private static DateOnly NextSlot() => new DateOnly(2001, 1, 7).AddDays(7 * Interlocked.Increment(ref _slots));

    private async Task<ReminderRunResult?> SendAsync(DateOnly slot)
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IOverdueReminders>()
            .SendForAsync(slot, CancellationToken.None);
    }

    private async Task<bool> ProcessAsync(ReminderOptions options)
    {
        using var scope = _factory.Services.CreateScope();
        var processor = new OverdueReminderProcessor(
            scope.ServiceProvider.GetRequiredService<IOverdueReminders>(),
            scope.ServiceProvider.GetRequiredService<IBusinessClock>(),
            Options.Create(options));
        return await processor.ProcessNextAsync(CancellationToken.None);
    }

    private Task<List<OverdueReminder>> RemindersAsync(Guid showroomId) =>
        Db(db => db.OverdueReminders.IgnoreQueryFilters([QueryFilters.Showroom]).AsNoTracking()
            .Where(r => r.ShowroomId == showroomId)
            .OrderBy(r => r.Date)
            .ToListAsync());

    private Task<Notification> NotificationAsync(Guid id) =>
        Db(db => db.Notifications.AsNoTracking().SingleAsync(n => n.Id == id));

    private async Task<T> Db<T>(Func<AppDbContext, Task<T>> query)
    {
        using var scope = _factory.Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private async Task DrainAsync<T>() where T : IQueueProcessor
    {
        for (var i = 0; i < 1000; i++)
        {
            using var scope = _factory.Services.CreateScope();
            if (!await scope.ServiceProvider.GetRequiredService<T>().ProcessNextAsync(CancellationToken.None))
                return;
        }
        Assert.Fail($"{typeof(T).Name} queue did not drain.");
    }

    private DateOnly Today() => _factory.Services.GetRequiredService<IBusinessClock>().Today;

    private string CompanyName() =>
        _factory.Services.GetRequiredService<IOptions<Settings.CompanySettings>>().Value.Name;

    private async Task<Showroom> SeedShowroomAsync(string phone)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var key = Guid.NewGuid().ToString("N")[..10];
        var showroom = new Showroom
        {
            Id = Guid.NewGuid(),
            Name = $"Reminder check {key}",
            PhoneNumber = phone,
            Address = "1 Test St",
            City = "Damietta",
            Country = "Egypt",
            TaxId = $"REMIND-{key}"
        };
        db.Showrooms.Add(showroom);
        await db.SaveChangesAsync();
        return showroom;
    }

    private async Task<Guid> DraftInvoiceAsync(string token, Guid showroomId, DateOnly dueDate)
    {
        var response = await SendAsync(HttpMethod.Post, "/api/invoices", token, new
        {
            showroomId,
            invoiceDate = dueDate.AddDays(-30),
            dueDate,
            lines = new[] { new { description = "Dining table", quantity = 1, unitPrice = 1000m, discount = 0m } }
        });
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await ReadJsonAsync(response)).GetProperty("id").GetGuid();
    }

    private async Task<Guid> IssuedInvoiceAsync(string token, Guid showroomId, DateOnly dueDate)
    {
        var invoiceId = await DraftInvoiceAsync(token, showroomId, dueDate);
        var issued = await SendAsync(HttpMethod.Patch, $"/api/invoices/{invoiceId}/issue", token);
        Assert.True(issued.StatusCode == HttpStatusCode.OK, await issued.Content.ReadAsStringAsync());
        return invoiceId;
    }

    private async Task PayAsync(string token, Guid invoiceId, decimal amount)
    {
        var response = await SendAsync(HttpMethod.Post, $"/api/invoices/{invoiceId}/payments", token,
            new { amount, method = "Cash" });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
    }

    private async Task CreditAllAsync(string token, Guid invoiceId)
    {
        var details = await SendAsync(HttpMethod.Get, $"/api/invoices/{invoiceId}", token);
        Assert.True(details.StatusCode == HttpStatusCode.OK, await details.Content.ReadAsStringAsync());
        var lineId = (await ReadJsonAsync(details)).GetProperty("lines")[0].GetProperty("id").GetGuid();

        var response = await SendAsync(HttpMethod.Post, $"/api/invoices/{invoiceId}/credit-notes", token, new
        {
            reason = "Returned in full",
            lines = new[] { new { invoiceLineId = lineId, netAmount = 1000m } }
        });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
    }

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body);
        return _client.SendAsync(request);
    }

    private async Task<string> TokenAsync()
    {
        var user = await _factory.CreateUserAsync(RoleIds.Accountant);
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
