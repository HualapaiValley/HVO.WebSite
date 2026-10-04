using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace HVO.WebSite.ApiTests;

[TestClass]
[DoNotParallelize] // Real host-environment cases restore process state before other tests run.
public sealed class ForwardedHeadersTrustTests
{
    [TestMethod]
    [DataRow("10.20.30.40", "10.20.30.40", null, "https", "203.0.113.7", "https", "203.0.113.7", false)]
    [DataRow("10.20.30.41", "10.20.30.40", null, "https", "203.0.113.7", "http", "10.20.30.41", false)]
    [DataRow("10.20.30.41", null, "10.20.30.0/24", "https", "203.0.113.7", "https", "203.0.113.7", false)]
    [DataRow("::ffff:10.20.30.41", null, "10.20.30.0/24", "https", "203.0.113.7", "https", "203.0.113.7", false)]
    [DataRow("127.0.0.1", null, null, "https", "203.0.113.7", "https", "203.0.113.7", true)]
    [DataRow("10.20.30.41", null, null, "https", "203.0.113.7", "http", "10.20.30.41", true)]
    [DataRow("10.20.30.40", "10.20.30.40", null, "https", "203.0.113.7", "https", "203.0.113.7", true)]
    [DataRow("10.20.30.41", "10.20.30.40", null, "https", "203.0.113.7", "http", "10.20.30.41", true)]
    [DataRow("10.20.30.40", "10.20.30.40", null, null, null, "http", "10.20.30.40", false)]
    [DataRow("10.20.30.40", "10.20.30.40", null, "https", "malformed", "http", "10.20.30.40", false)]
    [DataRow("10.20.30.40", "10.20.30.40", null, "https", null, "https", "10.20.30.40", false)]
    [DataRow("10.20.30.40", "10.20.30.40", null, "https, http", "203.0.113.7, 198.51.100.8", "http", "198.51.100.8", false)]
    [DataRow("::ffff:10.20.30.40", "10.20.30.40", null, "https", "203.0.113.7", "https", "203.0.113.7", false)]
    [DataRow("2001:db8::10", null, "2001:db8::/64", "https", "2001:db8:1::7", "https", "2001:db8:1::7", false)]
    [DataRow("10.20.30.40", "10.20.30.40", null, null, "203.0.113.7", "http", "203.0.113.7", false)]
    [DataRow("10.20.30.40", "10.20.30.40", null, "not a scheme", "203.0.113.7", "http", "203.0.113.7", false)]
    public async Task ActualHostProcessesOnlyNearestTrustedPeer(string peer, string? proxy, string? network,
        string? proto, string? forwardedFor, string scheme, string expectedPeer, bool hostForwarding)
    {
        using var factory = CreateFactory(peer, proxy, network, hostForwarding);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        if (hostForwarding) Assert.AreEqual("true", factory.Services.GetRequiredService<IConfiguration>()["ForwardedHeaders_Enabled"], "Exercise the framework host switch, not only application middleware.");
        var request = new HttpRequestMessage(HttpMethod.Get, "/__trust");
        if (proto is not null) request.Headers.TryAddWithoutValidation("X-Forwarded-Proto", proto);
        if (forwardedFor is not null) request.Headers.TryAddWithoutValidation("X-Forwarded-For", forwardedFor);
        var response = await client.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        var actual = await response.Content.ReadFromJsonAsync<Observed>();
        Assert.AreEqual(scheme, actual!.Scheme);
        Assert.AreEqual(expectedPeer, actual.Peer);
    }

    [TestMethod]
    [DataRow("KnownProxies", "not-an-ip")]
    [DataRow("KnownProxies", "0.0.0.0")]
    [DataRow("KnownNetworks", "10.1.2.0/nonsense")]
    [DataRow("KnownNetworks", "0.0.0.0/0")]
    public void MalformedTrustConfigurationFailsActualStartup(string section, string value)
    {
        using var factory = new IngestTrustTestFactory();
        factory.Configuration["ForwardedHeaders:Enabled"] = "true";
        factory.Configuration["ForwardedHeaders:" + section + ":0"] = value;
        var exception = Assert.ThrowsExactly<InvalidOperationException>(() => factory.CreateClient());
        StringAssert.Contains(exception.Message, "ForwardedHeaders:");
    }

    [TestMethod]
    [DataRow("10.20.30.40", "https")]
    [DataRow("10.20.30.41", "http")]
    public async Task EntraChallengeRedirectUsesTrustedScheme(string peer, string expectedScheme)
    {
        using var factory = CreateFactory(peer, "10.20.30.40", null, true);
        // Protocol metadata is local; this exercises the application's OIDC handler
        // without contacting Entra or using a real tenant.
        using var configured = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.PostConfigure<OpenIdConnectOptions>(OpenIdConnectDefaults.AuthenticationScheme, options =>
                options.Configuration = new OpenIdConnectConfiguration { AuthorizationEndpoint = "https://login.example.invalid/authorize", Issuer = "https://login.example.invalid" })));
        using var client = configured.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var request = new HttpRequestMessage(HttpMethod.Get, "/MicrosoftIdentity/Account/SignIn");
        request.Headers.Host = "observatory.example.invalid";
        request.Headers.Add("X-Forwarded-Proto", "https");
        request.Headers.Add("X-Forwarded-For", "203.0.113.7");
        var response = await client.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.Redirect, response.StatusCode, await response.Content.ReadAsStringAsync());
        var redirect = QueryHelpers.ParseQuery(response.Headers.Location!.Query)["redirect_uri"].ToString();
        Assert.AreEqual(expectedScheme, new Uri(redirect).Scheme);
        Assert.AreEqual("observatory.example.invalid", new Uri(redirect).Host);
        Assert.AreEqual("/signin-oidc", new Uri(redirect).AbsolutePath);
    }

    public static IEnumerable<object[]> ForwardingModes() =>
        from application in new[] { false, true }
        from host in new[] { false, true }
        from environment in new[] { false, true }
        from trust in new[] { "network", "proxies", "loopback", "unknown" }
        select new object[] { application, host, environment, trust };

    [TestMethod]
    [DynamicData(nameof(ForwardingModes))]
    public async Task EveryFlagCombinationConsumesAtMostOneTrustedHop(
        bool application, bool host, bool environment, string trust)
    {
        using var hostEnvironment = new ForwardingEnvironment(environment);
        var peer = trust switch { "unknown" => "10.20.31.40", "loopback" => "127.0.0.1", _ => "10.20.30.40" };
        var nearest = trust == "loopback" ? "127.0.0.2" : "10.20.30.41";
        using var factory = CreateModeFactory(peer, application, host, trust);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        if (host || environment)
            Assert.AreEqual("true", factory.Services.GetRequiredService<IConfiguration>()["ForwardedHeaders_Enabled"]);
        var request = new HttpRequestMessage(HttpMethod.Get, "/__trust");
        request.Headers.Add("X-Forwarded-For", "203.0.113.7, " + nearest);
        request.Headers.Add("X-Forwarded-Proto", "https, http");
        var response = await client.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var observed = await response.Content.ReadFromJsonAsync<Observed>();
        Assert.AreEqual("http", observed!.Scheme, "The older scheme must never be consumed.");
        Assert.AreEqual((application || host || environment) && trust != "unknown" ? nearest : peer, observed.Peer);
        if (application || host || environment)
        {
            var options = factory.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<Microsoft.AspNetCore.Builder.ForwardedHeadersOptions>>().Value;
            Assert.AreEqual(1, options.ForwardLimit);
            Assert.IsTrue(options.KnownProxies.Count + options.KnownIPNetworks.Count > 0, "Final trust lists must remain bounded for every switch combination.");
        }
    }

    public static IEnumerable<object[]> RedirectModes() =>
        from application in new[] { false, true }
        from host in new[] { false, true }
        from environment in new[] { false, true }
        from nearestScheme in new[] { "http", "https", "unknown" }
        select new object[] { application, host, environment, nearestScheme };

    [TestMethod]
    [DynamicData(nameof(RedirectModes))]
    public async Task EveryFlagCombinationKeepsEntraRedirectAtNearestTrustedScheme(
        bool application, bool host, bool environment, string nearestScheme)
    {
        using var hostEnvironment = new ForwardingEnvironment(environment);
        var unknownPeer = nearestScheme == "unknown";
        using var factory = CreateModeFactory(unknownPeer ? "10.20.31.40" : "10.20.30.40", application, host, "network");
        using var configured = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.PostConfigure<OpenIdConnectOptions>(OpenIdConnectDefaults.AuthenticationScheme, options =>
                options.Configuration = new OpenIdConnectConfiguration { AuthorizationEndpoint = "https://login.example.invalid/authorize", Issuer = "https://login.example.invalid" })));
        using var client = configured.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var request = new HttpRequestMessage(HttpMethod.Get, "/MicrosoftIdentity/Account/SignIn");
        request.Headers.Host = "observatory.example.invalid";
        request.Headers.Add("X-Forwarded-For", "203.0.113.7, 10.20.30.41");
        request.Headers.Add("X-Forwarded-Proto", nearestScheme == "http" ? "https, http" : "http, https");
        var response = await client.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.Redirect, response.StatusCode);
        var redirect = new Uri(QueryHelpers.ParseQuery(response.Headers.Location!.Query)["redirect_uri"].ToString());
        var expectedScheme = !unknownPeer && (application || host || environment) ? nearestScheme : "http";
        Assert.AreEqual(expectedScheme, redirect.Scheme);
        Assert.AreEqual("observatory.example.invalid", redirect.Host);
        Assert.AreEqual("/signin-oidc", redirect.AbsolutePath);
    }

    private static IngestTrustTestFactory CreateModeFactory(string peer, bool application, bool host, string trust)
    {
        var factory = new IngestTrustTestFactory { Peer = IPAddress.Parse(peer), HostForwarding = host };
        if (application) factory.Configuration["ForwardedHeaders:Enabled"] = "true";
        if (trust == "proxies")
        {
            factory.Configuration["ForwardedHeaders:KnownProxies:0"] = "10.20.30.40";
            factory.Configuration["ForwardedHeaders:KnownProxies:1"] = "10.20.30.41";
        }
        else if (trust != "loopback") factory.Configuration["ForwardedHeaders:KnownNetworks:0"] = "10.20.30.0/24";
        return factory;
    }

    private sealed class ForwardingEnvironment : IDisposable
    {
        private readonly string? _previous = Environment.GetEnvironmentVariable("ASPNETCORE_FORWARDEDHEADERS_ENABLED");
        public ForwardingEnvironment(bool enabled) => Environment.SetEnvironmentVariable("ASPNETCORE_FORWARDEDHEADERS_ENABLED", enabled ? "true" : null);
        public void Dispose() => Environment.SetEnvironmentVariable("ASPNETCORE_FORWARDEDHEADERS_ENABLED", _previous);
    }

    private static IngestTrustTestFactory CreateFactory(string peer, string? proxy, string? network, bool hostForwarding)
    {
        var factory = new IngestTrustTestFactory { Peer = IPAddress.Parse(peer), HostForwarding = hostForwarding };
        // Host-only mode deliberately leaves the application's explicit switch off.
        if (!hostForwarding) factory.Configuration["ForwardedHeaders:Enabled"] = "true";
        if (proxy is not null) factory.Configuration["ForwardedHeaders:KnownProxies:0"] = proxy;
        if (network is not null) factory.Configuration["ForwardedHeaders:KnownNetworks:0"] = network;
        return factory;
    }
    private sealed record Observed(string Scheme, string Peer);
}
