namespace ErpDashboard.Api.Features.Notifications;

public static partial class WhatsAppNumber
{
    [GeneratedRegex(@"^01[0125]\d{8}$")]
    private static partial Regex EgyptianMobile();

    public static string? TryNormalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var digits = new string(raw.Where(char.IsDigit).ToArray());

        if (digits.StartsWith("0020")) digits = "0" + digits[4..];
        else if (digits.StartsWith("20") && digits.Length == 12) digits = "0" + digits[2..];

        return EgyptianMobile().IsMatch(digits) ? "2" + digits : null;
    }
}