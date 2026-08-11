using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace HVO.Edge.Outbox;

public static class EdgeOutboxServiceCollectionExtensions
{
    public static IServiceCollection AddHvoEdgeOutbox(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<EdgeOutboxOptions>()
            .Bind(configuration.GetSection(EdgeOutboxOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<EdgeOutboxOptions>, EdgeOutboxOptionsValidator>());

        services.AddDbContext<DefaultEdgeOutboxDbContext>((provider, builder) =>
        {
            var path = provider.GetRequiredService<IOptions<EdgeOutboxOptions>>().Value.DatabasePath;
            var connectionString = new DbConnectionStringBuilder
            {
                ["Data Source"] = path,
            };
            builder.UseSqlite(connectionString.ConnectionString);
        });
        services.AddScoped<EdgeOutboxStore<DefaultEdgeOutboxDbContext>>();
        services.AddScoped<EdgeOutboxDiagnostics>();
        services.TryAddSingleton<RuntimeOutboxSettings>();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton(EdgeOutboxHealthOptions.Default);

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, EdgeOutboxInitializer>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, EdgeOutboxForwarder>());
        return services;
    }
}
