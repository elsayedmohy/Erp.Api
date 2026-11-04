namespace ErpDashboard.Api.Settings;

public sealed class CompanySettings
{
    public const string SectionName = "Company";
    public string Name { get; init; } = "";
    public string Address { get; init; } = "";
    public string TaxId { get; init; } = "";
    public string Currency { get; init; } = "EGP";
}