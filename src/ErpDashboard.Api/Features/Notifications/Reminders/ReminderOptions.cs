namespace ErpDashboard.Api.Features.Notifications.Reminders;

/// When the weekly overdue reminders go out, in the company's time zone (Company:TimeZone).
public sealed class ReminderOptions
{
    public const string SectionName = "Reminders";

    public DayOfWeek Day { get; init; } = DayOfWeek.Sunday;

    public TimeOnly SendAt { get; init; } = new(10, 0);

    [Range(0, 6)] public int CatchUpDays { get; init; } = 1;
}

public static class ReminderSchedule
{
    /// The weekly slot that should have been sent by localNow, or null when none is due.
    public static DateOnly? DueSlot(DateTime localNow, ReminderOptions options)
    {
        var today = DateOnly.FromDateTime(localNow);
        var daysSince = ((int)today.DayOfWeek - (int)options.Day + 7) % 7;
        var slot = today.AddDays(-daysSince);

        if (daysSince == 0 && TimeOnly.FromDateTime(localNow) < options.SendAt)
            slot = slot.AddDays(-7);   // today's slot is not due yet: the latest one is last week's

        return today.DayNumber - slot.DayNumber <= options.CatchUpDays ? slot : null;
    }
}
