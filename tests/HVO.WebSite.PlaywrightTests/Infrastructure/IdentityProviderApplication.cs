using System.Collections.Concurrent;
using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace HVO.WebSite.PlaywrightTests.Infrastructure;

/// <summary>Owned OIDC navigation targets; no discovery, credentials or external provider is needed.</summary>
internal sealed class IdentityProviderApplication : IDisposable, IAsyncDisposable
{
    public const string LandingPageText = "Test-owned identity provider landing page";
    private readonly WebApplication application;
    private readonly ConcurrentQueue<Uri> requests = new();

    public IdentityProviderApplication()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));
        builder.Logging.ClearProviders();
        application = builder.Build();
        application.Use((HttpContext context, RequestDelegate next) =>
        {
            requests.Enqueue(new Uri(context.Request.GetEncodedUrl()));
            return next(context);
        });
        application.MapGet("/oauth2/v2.0/authorize", () => Results.Text(LandingPageText));
        application.MapGet("/oauth2/v2.0/logout", () => Results.Text(LandingPageText));
        application.MapGet("/favicon.ico", () => Results.NoContent());
        try
        {
            application.Start();
            var addresses = application.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()
                ?? throw new InvalidOperationException("Identity-provider fixture did not publish its address.");
            Address = new Uri(addresses.Addresses.Single());
            if (!Address.IsLoopback)
                throw new InvalidOperationException("Identity-provider fixtures must bind only to loopback.");
        }
        catch
        {
            ((IDisposable)application).Dispose();
            throw;
        }
    }

    public Uri Address { get; }
    public Uri AuthorizationEndpoint => new(Address, "/oauth2/v2.0/authorize");
    public Uri LogoutEndpoint => new(Address, "/oauth2/v2.0/logout");

    public async Task AssertNavigationAsync(IPage page, Uri endpoint)
    {
        await page.WaitForURLAsync(endpoint + "?**");
        new Uri(page.Url).GetLeftPart(UriPartial.Path).Should().Be(endpoint.ToString());
        await Assertions.Expect(page.Locator("body")).ToHaveTextAsync(LandingPageText);
        requests.Should().Contain(new Uri(page.Url), "the owned provider must receive the actual redirected request");
    }

    public void Dispose() => ((IDisposable)application).Dispose();
    public async ValueTask DisposeAsync() => await application.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
}
