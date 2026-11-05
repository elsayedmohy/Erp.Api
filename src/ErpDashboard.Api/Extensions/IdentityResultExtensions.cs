namespace ErpDashboard.Api.Extensions;

public static class IdentityResultExtensions
{
    public static RequestValidationException ToValidationException(this IdentityResult result) =>
        new(result.Errors
            .GroupBy(e => e.Code.StartsWith("Password") ? "password"
                : e.Code.Contains("Email") || e.Code.Contains("UserName") ? "email"
                : "general")
            .ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray()));
}