namespace ErpDashboard.Api.Common;

public interface IBusinessClock
{
    DateOnly Today { get; }
    
    DateTime StartOfDayUtc(DateOnly date);
}

public sealed class BusinessClock : IBusinessClock
{
    private readonly TimeProvider _time;
    private readonly TimeZoneInfo _zone;

    public BusinessClock(TimeProvider time, IOptions<CompanySettings> company)
    {
        _time = time;
        _zone = TimeZoneInfo.FindSystemTimeZoneById(company.Value.TimeZone);
    }

    public DateOnly Today =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(_time.GetUtcNow(), _zone).DateTime);

    public DateTime StartOfDayUtc(DateOnly date)
    {
        var local = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        
        while (_zone.IsInvalidTime(local))
            local = local.AddMinutes(1);

        return TimeZoneInfo.ConvertTimeToUtc(local, _zone);
    }
}