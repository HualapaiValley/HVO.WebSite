using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using FluentAssertions;
using HVO.DataModels.Data;
using HVO.WebSite.v9;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HVO.WebSite.ApiTests;

/// <summary>
/// Integration tests verifying that the /admin page enforces the AdminOnly policy.
/// A lightweight TestAuthHandler is injected so tests can fabricate arbitrary
/// ClaimsPrincipal values via the X-Test-Role request header without touching
/// the real Entra OIDC flow.
/// </summary>
[TestClass]
public sealed class AdminPageAuthorizationTests
{
    private static AdminPageTestFactory _factory = null!;

    [ClassInitialize]
    public static void ClassInitialize(TestContext _)
    {
        _factory = new AdminPageTestFactory();
    }

    [ClassCleanup]
    public static void ClassCleanup()
    {
        _factory.Dispose();
    }

    // -------------------------------------------------------------------------
    // Unauthenticated — OIDC challenge redirect
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task AdminPage_Returns302_WhenUserIsUnauthenticated()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var response = await client.GetAsync("/admin");

        // Cookie auth challenges unauthenticated users by redirecting to the
        // Entra OIDC authorize endpoint (handled by Microsoft.Identity.Web).
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location.Should().NotBeNull();
        response.Headers.Location!.ToString().Should().NotContain("/admin");
    }

    [TestMethod]
    public async Task AdminPage_UsesForwardedHttpsScheme_ForOidcRedirectUri()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("http://hvo-website.test")
        });
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "203.0.113.10");
        client.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");

        var response = await client.GetAsync("/admin");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location.Should().NotBeNull();
        response.Headers.Location!.ToString().Should().Contain(Uri.EscapeDataString("https://hvo-website.test/signin-oidc"));
    }

    // -------------------------------------------------------------------------
    // Authenticated — wrong role → cookie Forbid → AccessDenied
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task AdminPage_Returns302ToAccessDenied_WhenUserHasUserRoleOnly()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        client.DefaultRequestHeaders.Add(TestAuthHandler.RoleHeader, AppRoles.User);

        var response = await client.GetAsync("/admin");

        // Authenticated but missing Admin role: cookie auth Forbid redirects
        // to the configured AccessDenied path.
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location?.ToString().Should().Contain("AccessDenied");
    }

    // -------------------------------------------------------------------------
    // Authenticated — correct role
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task AdminPage_Returns200_WhenUserHasAdminRole()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        client.DefaultRequestHeaders.Add(TestAuthHandler.RoleHeader, AppRoles.Admin);

        var response = await client.GetAsync("/admin");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("Administration");
    }

    // -------------------------------------------------------------------------
    // Test auth handler — reads X-Test-Role header to fabricate a ClaimsPrincipal
    // -------------------------------------------------------------------------

    internal sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public const string SchemeName = "TestScheme";
        public const string RoleHeader = "X-Test-Role";

        public TestAuthHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder)
            : base(options, logger, encoder) { }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(RoleHeader, out var roleValues))
                return Task.FromResult(AuthenticateResult.NoResult());

            var role = roleValues.FirstOrDefault() ?? string.Empty;

            var claims = new List<Claim>
            {
                new(ClaimTypes.Name, "Test User"),
                new(ClaimTypes.NameIdentifier, "test-user-id"),
            };

            if (!string.IsNullOrEmpty(role))
                claims.Add(new Claim(ClaimTypes.Role, role));

            var identity = new ClaimsIdentity(claims, SchemeName);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, SchemeName);

            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }

    // -------------------------------------------------------------------------
    // Test factory
    // -------------------------------------------------------------------------

    private sealed class AdminPageTestFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");

            builder.ConfigureAppConfiguration((_, configBuilder) =>
            {
                configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["KeyVault:Uri"] = string.Empty,
                    ["ForwardedHeaders:Enabled"] = "true",
                    ["ASPNETCORE_FORWARDEDHEADERS_ENABLED"] = "true",
                    ["AzureAd:ClientId"] = "00000000-0000-0000-0000-000000000001",
                    ["AzureAd:ClientSecret"] = "test-dummy-secret",
                    ["AzureAd:TenantId"] = "00000000-0000-0000-0000-000000000002",
                    ["ConnectionStrings:HualapaiValleyObservatory"] =
                        "Server=(localdb)\\MSSQLLocalDB;Database=_AdminTest;Trusted_Connection=True;",
                });
            });

            builder.ConfigureServices(services =>
            {
                // Register the test auth scheme alongside the existing Microsoft Identity Web schemes.
                // PostConfigure then promotes it to the default authenticate scheme so it runs
                // instead of the real cookie handler, keeping the Entra OIDC stack out of tests.
                services.AddAuthentication()
                    .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                        TestAuthHandler.SchemeName, _ => { });

                services.PostConfigure<AuthenticationOptions>(options =>
                {
                    options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                });

                ReplaceWithInMemory<HvoV9DbContext>(services, "v9-admintest");
                ReplaceWithInMemory<HvoDbContext>(services, "legacy-admintest");
            });
        }

        private static void ReplaceWithInMemory<TContext>(IServiceCollection services, string dbName)
            where TContext : DbContext
        {
            services.RemoveAll(typeof(DbContextOptions<TContext>));

            var toRemove = services
                .Where(d =>
                    d.ServiceType.IsGenericType &&
                    d.ServiceType.GetGenericArguments().Length == 1 &&
                    d.ServiceType.GetGenericArguments()[0] == typeof(TContext) &&
                    d.ServiceType.Name.StartsWith("IDbContextOptionsConfiguration", StringComparison.Ordinal))
                .ToList();

            foreach (var d in toRemove)
                services.Remove(d);

            services.AddDbContext<TContext>(opt => opt.UseInMemoryDatabase(dbName));
        }
    }
}
