namespace ErpDashboard.Api.Extensions;

public static class IdentityResultExtensions
{
    // Duplicate email/username is a 409; everything else is a 400 grouped by request field.
    public static AppException ToException(this IdentityResult result)
    {
        var duplicate = result.Errors.FirstOrDefault(e => e.Code is "DuplicateEmail" or "DuplicateUserName");
        if (duplicate is not null)
            return new ConflictException(duplicate.Description);

        var errors = result.Errors
            .GroupBy(e => FieldFor(e.Code))
            .ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray());

        return new RequestValidationException(errors);
    }

    private static string FieldFor(string code) => code switch
    {
        _ when code.StartsWith("Password") => "password",
        _ when code.Contains("Email") => "email",
        _ when code.Contains("UserName") => "email",
        _ => "user"
    };
}
