using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace HVO.WebSite.PlaywrightTests.Infrastructure;

/// <summary>A test-owned real application server, with an OS-assigned loopback port.</summary>
internal sealed class BrowserApplication<TEntryPoint> : IAsyncDisposable where TEntryPoint : class
{
    private readonly ApplicationFactory factory;

    public BrowserApplication(string project, Action<IWebHostBuilder>? configure = null)
    {
        factory = new ApplicationFactory(project, configure);
        factory.UseKestrel(0);
        try
        {
            factory.StartServer();
            var addresses = factory.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()
                ?? throw new InvalidOperationException("Browser fixture server did not publish its listening address.");
            Address = new Uri(addresses.Addresses.Single());
            if (!Address.IsLoopback)
                throw new InvalidOperationException("Browser fixtures must bind only to loopback.");
        }
        catch
        {
            factory.Dispose();
            throw;
        }
    }

    public Uri Address { get; }

    public static string RepositoryRoot
    {
        get
        {
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
                if (File.Exists(Path.Combine(directory.FullName, "HVO.WebSite.sln")))
                    return directory.FullName;
            throw new InvalidOperationException("Cannot locate the application source for browser fixtures.");
        }
    }

    public async ValueTask DisposeAsync() => await factory.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));

    private sealed class ApplicationFactory(string project, Action<IWebHostBuilder>? configure) : WebApplicationFactory<TEntryPoint>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseContentRoot(Path.Combine(RepositoryRoot, "src", project));
            builder.UseEnvironment("Development");
            builder.UseStaticWebAssets();
            configure?.Invoke(builder);
        }
    }
}
