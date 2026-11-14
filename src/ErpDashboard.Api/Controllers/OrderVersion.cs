namespace ErpDashboard.Api.Controllers;

internal static class OrderVersion
{
    public static uint Read(HttpRequest request)
    {
        var raw = request.Headers.IfMatch.ToString().Trim().Trim('"');
        if (raw.Length == 0)
            throw new PreconditionRequiredException("Send the order version in the If-Match header.");
        if (!uint.TryParse(raw, out var version))
            throw new RequestValidationException("If-Match", "The order version must be a number.");
        return version;
    }
}