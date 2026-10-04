using HVO.WebSite.v9;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using System.Net;
using HVO.DataModels.Data;
using HVO.WebSite.v9.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HVO.WebSite.ApiTests;

internal sealed class IngestTrustTestFactory : WebApplicationFactory<Program>
{
    private readonly string _database = Guid.NewGuid().ToString("N");
    internal Dictionary<string, string?> Configuration { get; } = new();
    internal IPAddress? Peer { get; init; }
    internal bool HostForwarding { get; init; }
    internal TimeProvider? Clock { get; init; }
    internal Func<HvoV9DbContext>? ContextFactory { get; init; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        if (HostForwarding) builder.UseSetting("FORWARDEDHEADERS_ENABLED", "true");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
            new Dictionary<string, string?>(Configuration)
            {
                ["KeyVault:Uri"] = "",
                ["EnableHttpsRedirect"] = "false",
                ["AzureAd:ClientId"] = "00000000-0000-0000-0000-000000000001",
                ["AzureAd:ClientSecret"] = "test-dummy-secret",
                ["AzureAd:TenantId"] = "00000000-0000-0000-0000-000000000002",
                ["ConnectionStrings:HualapaiValleyObservatory"] = "Server=(localdb)\\MSSQLLocalDB;Database=_TrustTest;Trusted_Connection=True;"
            }));
        builder.ConfigureServices(services =>
        {
            if (Clock is not null) { services.RemoveAll<TimeProvider>(); services.AddSingleton(Clock); }
            if (ContextFactory is { } createContext)
            {
                services.RemoveAll<HvoV9DbContext>();
                services.AddScoped(_ => createContext());
            }
            else Replace<HvoV9DbContext>(services, _database + "-v9");
            Replace<HvoDbContext>(services, _database + "-legacy");
            foreach (var descriptor in services.Where(d => d.ImplementationType == typeof(ApiKeySeedService)).ToArray())
                services.Remove(descriptor);
            if (Peer is not null) services.Insert(0, ServiceDescriptor.Singleton<IStartupFilter>(new PeerFilter(Peer)));
        });
    }

    private static void Replace<T>(IServiceCollection services, string name) where T : DbContext
    {
        services.RemoveAll<DbContextOptions<T>>();
        foreach (var descriptor in services.Where(d => d.ServiceType.IsGenericType
            && d.ServiceType.GetGenericArguments().Length == 1 && d.ServiceType.GetGenericArguments()[0] == typeof(T)
            && d.ServiceType.Name.StartsWith("IDbContextOptionsConfiguration", StringComparison.Ordinal)).ToArray())
            services.Remove(descriptor);
        services.AddDbContext<T>(options => options.UseInMemoryDatabase(name));
    }

    private sealed class PeerFilter(IPAddress peer) : IStartupFilter
    {
        public Action<Microsoft.AspNetCore.Builder.IApplicationBuilder> Configure(Action<Microsoft.AspNetCore.Builder.IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, continuation) => { context.Connection.RemoteIpAddress = peer; await continuation(); });
            next(app);
            // Endpoint routes execute inside the actual application middleware pipeline.
            app.UseEndpoints(endpoints => endpoints.MapGet("/__trust", async context =>
                await context.Response.WriteAsJsonAsync(new { scheme = context.Request.Scheme, peer = context.Connection.RemoteIpAddress?.ToString() })));
        };
    }
}
