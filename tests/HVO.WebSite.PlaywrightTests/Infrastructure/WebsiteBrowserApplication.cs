using System.Security.Claims;
using System.Text.Encodings.Web;
using HVO.DataModels.Data;
using HVO.Edge.Contracts.PowerSystem;
using HVO.WebSite.v9;
using HVO.WebSite.v9.Models;
using HVO.WebSite.v9.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace HVO.WebSite.PlaywrightTests.Infrastructure;

/// <summary>The real website host with test-owned data, clock and request authentication.</summary>
internal sealed class WebsiteBrowserApplication : IAsyncDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "hvo-website-browser-" + Guid.NewGuid().ToString("N"));
    private readonly BrowserApplication<HVO.WebSite.v9.Components.App> _application;
    public WebsitePowerState Power { get; } = new();
    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 10, 4, 0, 0, 0, TimeSpan.Zero));
    public Uri Address => _application.Address;

    public WebsiteBrowserApplication()
    {
        Directory.CreateDirectory(_directory);
        try
        {
            _application = new("HVO.WebSite.v9", builder =>
            {
                builder.UseEnvironment("Testing");
                builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["KeyVault:Uri"] = "",
                    ["AzureAd:Authority"] = "https://login.microsoftonline.com/00000000-0000-0000-0000-000000000002/v2.0",
                    ["AzureAd:TenantId"] = "00000000-0000-0000-0000-000000000002",
                    ["AzureAd:ClientId"] = "00000000-0000-0000-0000-000000000001",
                    ["AzureAd:ClientSecret"] = "test-only-dummy",
                    ["DataProtection:KeysDirectory"] = "", ["DataProtection:BlobUri"] = "", ["DataProtection:KeyIdentifier"] = "",
                    ["EnableHttpsRedirect"] = "false",
                    ["ForwardedHeaders:Enabled"] = "false",
                    ["ASPNETCORE_FORWARDEDHEADERS_ENABLED"] = "false",
                    ["ConnectionStrings:HualapaiValleyObservatory"] = "Server=(localdb)\\MSSQLLocalDB;Database=BrowserUnused;Trusted_Connection=True;",
                    ["PowerStatus:RefreshSeconds"] = "5", ["PowerStatus:HistoryRefreshSeconds"] = "60",
                    ["PowerComposition:Eg4BranchFreshnessSeconds"] = "10",
                    ["PowerComposition:ExpectedPvTrackerIds:0"] = "eg4-6500ex-a/mppt-1",
                    ["PowerComposition:ExpectedPvTrackerIds:1"] = "eg4-6500ex-a/mppt-2",
                    ["PowerComposition:ExpectedPvTrackerIds:2"] = "eg4-mppt100-48hv-a/mppt-1",
                }));
                builder.ConfigureServices(services =>
                {
                    ReplaceDatabase<HvoV9DbContext>(services, "v9-" + Guid.NewGuid());
                    ReplaceDatabase<HvoDbContext>(services, "legacy-" + Guid.NewGuid());
                    services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(_directory));
                    services.RemoveAll<TimeProvider>();
                    services.AddSingleton<TimeProvider>(Clock);
                    services.AddSingleton(Power);
                    services.RemoveAll<IPowerSystemSnapshotProvider>();
                    services.RemoveAll<IPowerInventoryConfigurationProvider>();
                    services.AddScoped<IPowerSystemSnapshotProvider, WebsiteSnapshotProvider>();
                    services.AddScoped<IPowerInventoryConfigurationProvider, WebsiteInventoryProvider>();
                    services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, BrowserAuthenticationHandler>(BrowserAuthenticationHandler.SchemeName, _ => { });
                    services.PostConfigure<AuthenticationOptions>(options => options.DefaultAuthenticateScheme = BrowserAuthenticationHandler.SchemeName);
                    // Preserve the real OIDC challenge handler, without contacting discovery/the identity provider.
                    services.PostConfigure<OpenIdConnectOptions>(OpenIdConnectDefaults.AuthenticationScheme, options =>
                    {
                        options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(new()
                        {
                            Issuer = "https://login.microsoftonline.com/00000000-0000-0000-0000-000000000002/v2.0",
                            AuthorizationEndpoint = "https://login.microsoftonline.com/00000000-0000-0000-0000-000000000002/oauth2/v2.0/authorize",
                        });
                    });
                });
            });
        }
        catch { Directory.Delete(_directory, recursive: true); throw; }
    }

    private static void ReplaceDatabase<T>(IServiceCollection services, string name) where T : DbContext
    {
        services.RemoveAll<DbContextOptions<T>>();
        foreach (var descriptor in services.Where(descriptor => descriptor.ServiceType.IsGenericType
            && descriptor.ServiceType.GetGenericArguments().SequenceEqual([typeof(T)])
            && descriptor.ServiceType.Name.StartsWith("IDbContextOptionsConfiguration", StringComparison.Ordinal)).ToArray())
            services.Remove(descriptor);
        services.AddDbContext<T>(options => options.UseInMemoryDatabase(name));
    }

    public async ValueTask DisposeAsync()
    {
        try { await _application.DisposeAsync(); }
        finally { Directory.Delete(_directory, recursive: true); }
    }

    private sealed class BrowserAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "WebsiteBrowser";
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Test-Role", out var role)) return Task.FromResult(AuthenticateResult.NoResult());
            var claims = new List<Claim> { new(ClaimTypes.Name, "Browser User"), new(ClaimTypes.NameIdentifier, "browser-user") };
            if (!string.IsNullOrWhiteSpace(role)) claims.Add(new(ClaimTypes.Role, role.ToString()));
            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
        }
    }
}

internal sealed record WebsitePowerData(PowerSystemSnapshot? Snapshot, PowerTelemetryHistoryResponse History,
    string? Failure = null);

internal sealed class WebsitePowerState
{
    private WebsitePowerData _data = new(null, PowerTelemetryHistoryResponse.Empty);
    private int _currentCalls;
    private int _historyCalls;
    public WebsitePowerData Data { get => Volatile.Read(ref _data); set => Volatile.Write(ref _data, value); }
    public int CurrentCalls => Volatile.Read(ref _currentCalls);
    public int HistoryCalls => Volatile.Read(ref _historyCalls);
    public void CurrentRead() => Interlocked.Increment(ref _currentCalls);
    public void HistoryRead() => Interlocked.Increment(ref _historyCalls);
    public void Check(string section) { if (Data.Failure == section) throw new InvalidOperationException("Test provider failure after SQL retries"); }

    public static WebsitePowerData Populated(DateTime observed, bool alarm = false) => new(
        new(observed,
            Pv: new(new(1000, PowerMetricSource.Eg46500Ex, observed)),
            Battery: new(PowerW: new(-500, PowerMetricSource.Eg46500Ex, observed), HasAlarms: new(alarm, PowerMetricSource.Eg46500Ex, observed)),
            BatteryObservations: [new("eg4-6500ex-a", "inverter", PowerMetricSource.Eg46500Ex, PowerMeasurementRole.InverterBranch, "battery", observed, PowerW: -500)]),
        new([new() { SourceId = "eg4-6500ex-a", RecordedAtUtc = observed, Trackers = [new() { TrackerId = "mppt-1", PowerW = 1000 }] }],
            [new(observed, "eg4-6500ex-a", "inverter", -500)]));
}

internal sealed class WebsiteSnapshotProvider(WebsitePowerState state) : IPowerSystemSnapshotProvider
{
    public Task<PowerSystemSnapshot?> GetLatestAsync(int lookbackMinutes = 60, CancellationToken ct = default)
    { ct.ThrowIfCancellationRequested(); state.CurrentRead(); state.Check("current"); return Task.FromResult(state.Data.Snapshot); }
}

internal sealed class WebsiteInventoryProvider(WebsitePowerState state, TimeProvider clock) : IPowerInventoryConfigurationProvider
{
    public Task<(PowerDeviceInventorySnapshotResponse Inventory, PowerConfigurationSnapshotResponse Configuration)> GetLatestAsync(string sourceId = "solarassistant-total", int staleAfterMinutes = 1440, CancellationToken ct = default)
        => throw new NotSupportedException();
    public Task<PowerInverterDetailSnapshotResponse> GetLatestInverterDetailAsync(string sourceId, int staleAfterMinutes = 5, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested(); state.Check("inverter");
        return Task.FromResult(new PowerInverterDetailSnapshotResponse { SourceId = sourceId, RecordedAtUtc = clock.GetUtcNow().UtcDateTime,
            IsPresent = true, PvStrings = [new() { StringId = "mppt-1", PowerW = 1234 }] });
    }
    public Task<PowerMpptDetailSnapshotResponse> GetLatestMpptDetailAsync(string sourceId, int staleAfterMinutes = 5, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested(); state.Check("controller");
        return Task.FromResult(new PowerMpptDetailSnapshotResponse { SourceId = sourceId, RecordedAtUtc = clock.GetUtcNow().UtcDateTime,
            IsPresent = true, Trackers = [new() { TrackerId = "mppt-1", Name = "External MPPT", PowerW = 567 }] });
    }
    public Task<PowerTelemetryHistoryResponse> GetRecentTelemetryAsync(IReadOnlyCollection<string> mpptSourceIds, IReadOnlyCollection<string> batterySourceIds, DateTime sinceUtc, CancellationToken ct = default)
    { ct.ThrowIfCancellationRequested(); state.HistoryRead(); state.Check("history"); return Task.FromResult(state.Data.History); }
}
