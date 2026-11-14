using ErpDashboard.Api.Common;
using ErpDashboard.Api.Settings;
using Microsoft.Extensions.Options;

namespace ErpDashboard.Api.UnitTests;

public class BusinessClockTests
{
    private sealed class FixedTime(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private static BusinessClock ClockAt(string utc) =>
        new(new FixedTime(DateTimeOffset.Parse(utc)),
            Options.Create(new CompanySettings { TimeZone = "Africa/Cairo" }));

    [Theory]
    [InlineData("2026-11-14T21:59:00Z", "2026-11-14")] // 23:59 Cairo (UTC+2, winter)
    [InlineData("2026-11-14T22:00:00Z", "2026-11-15")] // 00:00 Cairo, next day
    [InlineData("2026-07-10T20:59:00Z", "2026-07-10")] // 23:59 Cairo (UTC+3, summer)
    [InlineData("2026-07-10T21:00:00Z", "2026-07-11")] // 00:00 Cairo, next day
    [InlineData("2026-12-31T22:30:00Z", "2027-01-01")] // the invoice-number year case
    public void Today_UsesCairoDate(string utcNow, string expected)
    {
        Assert.Equal(DateOnly.Parse(expected), ClockAt(utcNow).Today);
    }

    [Theory]
    [InlineData("2026-11-15", "2026-11-14T22:00:00Z")] // winter: UTC+2
    [InlineData("2026-07-11", "2026-07-10T21:00:00Z")] // summer: UTC+3
    public void StartOfDayUtc_IsLocalMidnight(string date, string expectedUtc)
    {
        var actual = ClockAt("2026-01-01T00:00:00Z").StartOfDayUtc(DateOnly.Parse(date));

        Assert.Equal(DateTime.Parse(expectedUtc).ToUniversalTime(), actual);
        Assert.Equal(DateTimeKind.Utc, actual.Kind);
    }

    [Fact]
    public void StartOfDayUtc_OnDaylightSavingStart_DoesNotThrow_AndStartsAtFirstValidMinute()
    {
        // Egypt moves clocks from 00:00 to 01:00 on the last Friday of April.
        // On 2026-04-24, 00:00–00:59 does not exist; the day starts at 01:00 (UTC+3) = 22:00 UTC the day before.
        var actual = ClockAt("2026-01-01T00:00:00Z").StartOfDayUtc(new DateOnly(2026, 4, 24));

        Assert.Equal(new DateTime(2026, 4, 23, 22, 0, 0, DateTimeKind.Utc), actual);
    }

    [Fact]
    public void ConsecutiveDays_HaveNoGapOrOverlap()
    {
        var clock = ClockAt("2026-01-01T00:00:00Z");
        var day = new DateOnly(2026, 1, 1);

        for (var i = 0; i < 366; i++, day = day.AddDays(1))
        {
            var start = clock.StartOfDayUtc(day);
            var nextStart = clock.StartOfDayUtc(day.AddDays(1));
            Assert.True(nextStart > start, $"{day}: next day must start after this one.");
            Assert.InRange((nextStart - start).TotalHours, 23, 25);
        }
    }
}