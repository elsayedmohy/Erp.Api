namespace ErpDashboard.Api.Settings;

public sealed class FinanceSettings
{
    public const string SectionName = "Finance";
    public decimal DefaultTaxRate { get; init; } = 14m;
    public int DefaultDueDays { get; init; } = 30;
}