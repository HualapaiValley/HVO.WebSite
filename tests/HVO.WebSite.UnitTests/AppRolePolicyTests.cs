using FluentAssertions;
using HVO.WebSite.v9;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Security.Claims;

namespace HVO.WebSite.UnitTests;

[TestClass]
public class AppRolePolicyTests
{
    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static IAuthorizationService BuildAuthorizationService(Action<AuthorizationOptions> configure)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorizationCore(configure);
        return services.BuildServiceProvider().GetRequiredService<IAuthorizationService>();
    }

    private static ClaimsPrincipal UserWithRoles(params string[] roles)
    {
        var claims = new List<Claim> { new Claim(ClaimTypes.Name, "test@example.com") };
        foreach (var role in roles)
            claims.Add(new Claim(ClaimTypes.Role, role));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
    }

    private static ClaimsPrincipal AnonymousUser() =>
        new ClaimsPrincipal(new ClaimsIdentity());

    // -------------------------------------------------------------------------
    // AppRoles constants
    // -------------------------------------------------------------------------

    [TestMethod]
    public void AppRoles_Admin_HasExpectedValue() =>
        AppRoles.Admin.Should().Be("Admin");

    [TestMethod]
    public void AppRoles_User_HasExpectedValue() =>
        AppRoles.User.Should().Be("User");

    // -------------------------------------------------------------------------
    // AdminOnly policy
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task AdminOnly_AllowsAdminRole()
    {
        var authz = BuildAuthorizationService(o =>
            o.AddPolicy("AdminOnly", p => p.RequireRole(AppRoles.Admin)));

        var result = await authz.AuthorizeAsync(UserWithRoles("Admin"), null, "AdminOnly");

        result.Succeeded.Should().BeTrue();
    }

    [TestMethod]
    public async Task AdminOnly_DeniesUserRole()
    {
        var authz = BuildAuthorizationService(o =>
            o.AddPolicy("AdminOnly", p => p.RequireRole(AppRoles.Admin)));

        var result = await authz.AuthorizeAsync(UserWithRoles("User"), null, "AdminOnly");

        result.Succeeded.Should().BeFalse();
    }

    [TestMethod]
    public async Task AdminOnly_DeniesAnonymous()
    {
        var authz = BuildAuthorizationService(o =>
            o.AddPolicy("AdminOnly", p => p.RequireRole(AppRoles.Admin)));

        var result = await authz.AuthorizeAsync(AnonymousUser(), null, "AdminOnly");

        result.Succeeded.Should().BeFalse();
    }

    [TestMethod]
    public async Task AdminOnly_DeniesUserWithNoRoles()
    {
        var authz = BuildAuthorizationService(o =>
            o.AddPolicy("AdminOnly", p => p.RequireRole(AppRoles.Admin)));

        var result = await authz.AuthorizeAsync(UserWithRoles(), null, "AdminOnly");

        result.Succeeded.Should().BeFalse();
    }

    // -------------------------------------------------------------------------
    // UserOrAdmin policy
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task UserOrAdmin_AllowsAdminRole()
    {
        var authz = BuildAuthorizationService(o =>
            o.AddPolicy("UserOrAdmin", p => p.RequireRole(AppRoles.User, AppRoles.Admin)));

        var result = await authz.AuthorizeAsync(UserWithRoles("Admin"), null, "UserOrAdmin");

        result.Succeeded.Should().BeTrue();
    }

    [TestMethod]
    public async Task UserOrAdmin_AllowsUserRole()
    {
        var authz = BuildAuthorizationService(o =>
            o.AddPolicy("UserOrAdmin", p => p.RequireRole(AppRoles.User, AppRoles.Admin)));

        var result = await authz.AuthorizeAsync(UserWithRoles("User"), null, "UserOrAdmin");

        result.Succeeded.Should().BeTrue();
    }

    [TestMethod]
    public async Task UserOrAdmin_DeniesAnonymous()
    {
        var authz = BuildAuthorizationService(o =>
            o.AddPolicy("UserOrAdmin", p => p.RequireRole(AppRoles.User, AppRoles.Admin)));

        var result = await authz.AuthorizeAsync(AnonymousUser(), null, "UserOrAdmin");

        result.Succeeded.Should().BeFalse();
    }

    [TestMethod]
    public async Task UserOrAdmin_DeniesUserWithNoRoles()
    {
        var authz = BuildAuthorizationService(o =>
            o.AddPolicy("UserOrAdmin", p => p.RequireRole(AppRoles.User, AppRoles.Admin)));

        var result = await authz.AuthorizeAsync(UserWithRoles(), null, "UserOrAdmin");

        result.Succeeded.Should().BeFalse();
    }
}
