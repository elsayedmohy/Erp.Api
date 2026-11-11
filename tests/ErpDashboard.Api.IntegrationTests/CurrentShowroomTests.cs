using System.Security.Claims;
using ErpDashboard.Api.Common.Authorization;
using Microsoft.AspNetCore.Http;

public class CurrentShowroomTests
{
    private static CurrentShowroom Create(HttpContext? http) =>
        new(new HttpContextAccessor { HttpContext = http });

    private static DefaultHttpContext Authenticated(Guid? showroomId = null, params string[] permissions)
    {
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())], "Test"))
        };
        http.Items[PermissionHandler.ItemsKey] = new HashSet<string>(permissions, StringComparer.Ordinal);
        if (showroomId is { } id)
            http.Items[CurrentShowroom.ItemsKey] = id;
        return http;
    }

    [Fact]
    public void NoHttpContext_IsNone() =>
        Assert.Equal(ShowroomAccess.None, Create(null).Current);

    [Fact]
    public void AccessAllPermission_IsAccessAll() =>
        Assert.Equal(ShowroomAccess.All, Create(Authenticated(null, PermissionCodes.Showrooms.AccessAll)).Current);

    [Fact]
    public void ShowroomId_IsScoped_EvenWithAccessAll()
    {
        var id = Guid.NewGuid();
        Assert.Equal(ShowroomAccess.For(id), Create(Authenticated(id, PermissionCodes.Showrooms.AccessAll)).Current);
    }

    [Fact]
    public void AuthenticatedWithoutShowroomOrPermission_IsNone() =>
        Assert.Equal(ShowroomAccess.None, Create(Authenticated()).Current);

    [Fact]
    public void ReadsItemsLazily()
    {
        var http = Authenticated();
        var current = Create(http);
        Assert.Equal(ShowroomAccess.None, current.Current);

        http.Items[PermissionHandler.ItemsKey] = new HashSet<string> { PermissionCodes.Showrooms.AccessAll };

        Assert.Equal(ShowroomAccess.All, current.Current);
    }
}