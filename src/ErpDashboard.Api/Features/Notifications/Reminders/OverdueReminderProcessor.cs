using Microsoft.Extensions.Options;

namespace ErpDashboard.Api.Features.Notifications.Reminders;

public sealed class OverdueReminderProcessor(
    IOverdueReminders reminders,
    IBusinessClock clock,
    IOptions<ReminderOptions> options) : IQueueProcessor
{
    public async Task<bool> ProcessNextAsync(CancellationToken ct)
    {
        var slot = ReminderSchedule.DueSlot(clock.LocalNow, options.Value);
        if (slot is not null)
            await reminders.SendForAsync(slot.Value, ct);

        return false;
    }
}
