namespace ErpDashboard.Api.Settings;

public class JwtSettings
{
    public string Key { get; set; } = default!;
    public string Issuer { get; set; } = default!;
    public string Audience { get; set; } = default!;
    public int DurationInMinutes { get; set; } = default!;
    public int ExpiryDays { get; set; } = default!;
}