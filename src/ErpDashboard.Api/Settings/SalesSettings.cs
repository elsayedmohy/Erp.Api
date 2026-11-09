namespace ErpDashboard.Api.Settings;

public sealed class SalesSettings
{
    public const string SectionName = "Sales";
    public decimal DiscountApprovalThresholdPercent { get; init; } = 10;
}