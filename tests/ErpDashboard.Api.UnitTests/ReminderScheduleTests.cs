using ErpDashboard.Api.Features.Notifications.Reminders;

namespace ErpDashboard.Api.UnitTests;

public class ReminderScheduleTests
{
    // Sunday 10:00, one day of catch-up. 2026-11-22 is a Sunday.
    private static readonly ReminderOptions Sunday10 = new()
    {
        Day = DayOfWeek.Sunday,
        SendAt = new TimeOnly(10, 0),
        CatchUpDays = 1
    };

    [Theory]
    [InlineData("2026-11-22T10:00:00", "2026-11-22")] // on the minute
    [InlineData("2026-11-22T18:30:00", "2026-11-22")] // later that day
    [InlineData("2026-11-23T08:00:00", "2026-11-22")] // Monday: catching up a missed Sunday
    [InlineData("2026-11-23T23:59:00", "2026-11-22")]
    public void DueSlot_IsTheLatestSunday_WithinTheCatchUp(string localNow, string expected)
    {
        Assert.Equal(DateOnly.Parse(expected), ReminderSchedule.DueSlot(DateTime.Parse(localNow), Sunday10));
    }

    [Theory]
    [InlineData("2026-11-22T09:59:00")] // Sunday before 10:00: last Sunday is 7 days old
    [InlineData("2026-11-24T00:00:00")] // Tuesday: too late to catch up
    [InlineData("2026-11-28T23:59:00")] // Saturday
    public void DueSlot_IsNull_OutsideTheWindow(string localNow)
    {
        Assert.Null(ReminderSchedule.DueSlot(DateTime.Parse(localNow), Sunday10));
    }

    [Fact]
    public void NoCatchUp_OnlyTheDayItself()
    {
        var strict = new ReminderOptions { Day = DayOfWeek.Sunday, SendAt = new TimeOnly(10, 0), CatchUpDays = 0 };

        Assert.Equal(new DateOnly(2026, 11, 22), ReminderSchedule.DueSlot(new DateTime(2026, 11, 22, 10, 0, 0), strict));
        Assert.Null(ReminderSchedule.DueSlot(new DateTime(2026, 11, 23, 8, 0, 0), strict));
    }

    [Fact]
    public void OtherDay_Wednesday()
    {
        var wednesday = new ReminderOptions { Day = DayOfWeek.Wednesday, SendAt = new TimeOnly(9, 0), CatchUpDays = 1 };

        // 2026-11-25 is a Wednesday.
        Assert.Equal(new DateOnly(2026, 11, 25), ReminderSchedule.DueSlot(new DateTime(2026, 11, 25, 9, 0, 0), wednesday));
        Assert.Null(ReminderSchedule.DueSlot(new DateTime(2026, 11, 22, 10, 0, 0), wednesday));
    }

    [Fact]
    public void Message_HasTheCountAmountAndDate_InWesternDigits()
    {
        var text = OverdueReminders.Message("Nile Furniture", 3, 12345.5m, new DateOnly(2026, 10, 1), "Damietta Woodworks");

        Assert.Contains("Nile Furniture", text);
        Assert.Contains("3", text);
        Assert.Contains("12,345.50", text);
        Assert.Contains("2026-10-01", text);
        Assert.EndsWith("Damietta Woodworks", text);
    }

    [Fact]
    public void Message_WithoutACompanyName_HasNoEmptyLastLine()
    {
        var text = OverdueReminders.Message("Nile Furniture", 1, 100m, new DateOnly(2026, 10, 1), "");

        Assert.EndsWith("برجاء السداد أو التواصل مع قسم الحسابات.", text);
    }
}
