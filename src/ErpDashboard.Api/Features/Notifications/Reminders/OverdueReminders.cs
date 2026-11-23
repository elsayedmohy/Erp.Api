using ErpDashboard.Api.Settings;
using Microsoft.Extensions.Options;

namespace ErpDashboard.Api.Features.Notifications.Reminders;

public sealed record ReminderRunResult(int Showrooms, int Skipped);

public interface IOverdueReminders
{
    Task<ReminderRunResult?> SendForAsync(DateOnly slot, CancellationToken ct);
}


public sealed class OverdueReminders(
    AppDbContext dbContext,
    IBusinessClock clock,
    IOptions<CompanySettings> company,
    ILogger<OverdueReminders> logger) : IOverdueReminders
{
    public async Task<ReminderRunResult?> SendForAsync(DateOnly slot, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        await using var transaction = await dbContext.Database.BeginTransactionAsync(ct);


        var claimed = await dbContext.Database.ExecuteSqlAsync($"""
            INSERT INTO "ReminderRuns" ("Date", "StartedAt", "Showrooms", "Skipped")
            VALUES ({slot}, {now}, 0, 0)
            ON CONFLICT ("Date") DO NOTHING
            """, ct);
        if (claimed == 0)
            return null;

        var today = clock.Today;


        var owing = await dbContext.AllBalances()
            .IgnoreQueryFilters([QueryFilters.Showroom])
            .Where(b => b.Invoice.Status == InvoiceStatus.Issued && b.Invoice.DueDate < today)
            .Where(InvoicePaymentRules.HasStatus(InvoicePaymentStatus.Overdue, today))
            .GroupBy(b => b.Invoice.ShowroomId)
            .Select(g => new
            {
                ShowroomId = g.Key,
                Count = g.Count(),
                Amount = g.Sum(b => b.Invoice.TotalAmount - b.Paid - b.Credited),
                Oldest = g.Min(b => b.Invoice.DueDate)
            })
            .ToListAsync(ct);

        var ids = owing.Select(o => o.ShowroomId).ToList();
        var showrooms = await dbContext.Showrooms
            .IgnoreQueryFilters([QueryFilters.Showroom]).AsNoTracking()
            .Select(s => new { s.Id, s.Name, s.PhoneNumber })
            .ToDictionaryAsync(s => s.Id, ct);

        var queued = 0;
        var skipped = 0;
        foreach (var o in owing)
        {
            if (!showrooms.TryGetValue(o.ShowroomId, out var showroom))
                continue;

            var recipient = WhatsAppNumber.TryNormalize(showroom.PhoneNumber);
            var notification = new Notification
            {
                Id = Guid.NewGuid(),
                Recipient = recipient,
                Body = Message(showroom.Name, o.Count, o.Amount, o.Oldest, company.Value.Name),
                Status = recipient is null ? NotificationStatus.Skipped : NotificationStatus.Pending,
                Error = recipient is null ? "The showroom phone is not an Egyptian mobile number." : null,
                NextAttemptAt = recipient is null ? null : now,
                CreatedAt = now
            };
            dbContext.Notifications.Add(notification);
            dbContext.OverdueReminders.Add(new OverdueReminder
            {
                Id = Guid.NewGuid(),
                ShowroomId = o.ShowroomId,
                Date = slot,
                InvoiceCount = o.Count,
                Amount = o.Amount,
                OldestDueDate = o.Oldest,
                NotificationId = notification.Id,
                CreatedAt = now
            });

            queued++;
            if (recipient is null) skipped++;
        }

        await dbContext.SaveChangesAsync(ct);
        await dbContext.ReminderRuns.Where(r => r.Date == slot)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Showrooms, queued)
                .SetProperty(r => r.Skipped, skipped), ct);
        await transaction.CommitAsync(ct);

        // Counts only: no names or phone numbers in the logs.
        logger.LogInformation(
            "Overdue reminders for {Slot}: {Showrooms} showrooms, {Skipped} without a mobile number",
            slot, queued, skipped);
        return new ReminderRunResult(queued, skipped);
    }

    public static string Message(string showroom, int invoices, decimal amount, DateOnly oldestDueDate, string company)
    {
        var egp = amount.ToString("N2", CultureInfo.InvariantCulture);
        var due = oldestDueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var signature = string.IsNullOrWhiteSpace(company) ? "" : $"\n{company}";
        return $"""
            السادة {showroom}،
            عدد الفواتير المتأخرة: {invoices}، بإجمالي {egp} جنيه.
            أقدمها كان مستحق السداد في {due}.
            برجاء السداد أو التواصل مع قسم الحسابات.{signature}
            """;
    }
}